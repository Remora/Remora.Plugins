//
//  PluginService.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Remora.Plugins.Abstractions;
using Remora.Plugins.Abstractions.Attributes;
using Remora.Plugins.Errors;
using Remora.Plugins.Extensions;
using Remora.Results;

namespace Remora.Plugins.Services;

/// <summary>
/// Serves functionality related to plugins.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PluginService"/> class.
/// </remarks>
/// <param name="options">The service options.</param>
[PublicAPI]
public sealed class PluginService(PluginServiceOptions? options = null)
{
    private readonly PluginServiceOptions _options = options ?? PluginServiceOptions.Default;

    private Dictionary<Assembly, IEnumerable<Type>>? _pluginsByAssembly;

    /// <summary>
    /// Loads all available plugins into a tree structure, ordered by their topological dependencies. Effectively, this
    /// means that <see cref="PluginTree.Branches"/> will contain dependency-free plugins, with subsequent
    /// dependents below them (recursively).
    /// </summary>
    /// <param name="services">The service collection with which to register the plugin tree.</param>
    /// <param name="filter">If provided, any plugins must match the defined predicate to be added to the <see cref="PluginTree"/>.</param>
    /// <returns>The dependency tree.</returns>
    [PublicAPI, Pure]
    public PluginTreeBuilder LoadPluginTree(IServiceCollection services, Predicate<IPluginDescriptor>? filter = null)
    {
        filter ??= _ => true;
        LoadAvailablePluginAssemblies();

        var pluginsWithDependencies = _pluginsByAssembly.Keys.ToDictionary
        (
            a => a,
            a => a.GetReferencedAssemblies()
                .Where(ra => _pluginsByAssembly.Keys.Any(pa => pa.FullName == ra.FullName))
                .Select(ra => _pluginsByAssembly.Keys.First(pa => pa.FullName == ra.FullName))
        );

        // Load plugin dependencies.
        foreach (IEnumerable<Type> plugins in _pluginsByAssembly.Values)
        {
            MethodInfo configureHelper = typeof(PluginService).GetMethod(nameof(ConfigurePlugin))
                ?? throw new InvalidOperationException(); // This will never be null.

            foreach (var plugin in plugins)
            {
                configureHelper.MakeGenericMethod(plugin).Invoke(null, [services]);
            }
        }

        // Build and populate plugin tree.
        var tree = new PluginTreeBuilder(filter);
        var nodes = new Dictionary<Assembly, PluginTreeNodeBuilder>();

        var sorted = pluginsWithDependencies.Keys.TopologicalSort(k => pluginsWithDependencies[k]).ToList();
        while (sorted.Count > 0)
        {
            Assembly current = sorted[0];
            IEnumerable<Type> pluginTypes = _pluginsByAssembly[current];

            foreach (var type in pluginTypes)
            {
                var treeNodeBuilder = new PluginTreeNodeBuilder(current, type);
                var dependencies = pluginsWithDependencies[current].ToList();

                if (!dependencies.Any())
                {
                    tree.AddTreeNode(treeNodeBuilder);
                }

                foreach (var dependency in dependencies)
                {
                    if (!IsDirectDependency(current, dependency))
                    {
                        continue;
                    }

                    var dependencyNode = nodes[dependency];
                    dependencyNode.AddDependent(treeNodeBuilder);
                }

                nodes.Add(current, treeNodeBuilder);
                sorted.Remove(current);
            }
        }

        return tree;

        bool IsDirectDependency(Assembly assembly, Assembly dependency)
        {
            var dependencies = pluginsWithDependencies[assembly];
            return IsDependency(assembly, dependency) && dependencies.All(d => !IsDependency(d, dependency));
        }

        bool IsDependency(Assembly assembly, Assembly other)
        {
            var dependencies = pluginsWithDependencies[assembly];
            foreach (var dependency in dependencies)
            {
                if (dependency == other)
                {
                    return true;
                }

                if (IsDependency(dependency, other))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Loads all available plugins into a flat list.
    /// </summary>
    /// <param name="services">The service provider used to build the plugins.</param>
    /// <remarks>
    /// This method should generally not be used for actually loading plugins into your application, since it may not
    /// properly order plugins in more complex dependency graphs. Prefer using <see cref="LoadPluginTree"/> and its
    /// associated methods.
    /// </remarks>
    /// <returns>The descriptors of the available plugins.</returns>
    [Pure]
    public IEnumerable<IPluginDescriptor> LoadPlugins(IServiceProvider services)
    {
        LoadAvailablePluginAssemblies();
        var pluginsWithDependencies = _pluginsByAssembly.Keys.ToDictionary
        (
            a => a,
            a => a.GetReferencedAssemblies()
                .Where(ra => _pluginsByAssembly.Keys.Any(pa => pa.FullName == ra.FullName))
                .Select(ra => _pluginsByAssembly.Keys.First(pa => pa.FullName == ra.FullName))
        );

        foreach ((Assembly assembly, IEnumerable<Assembly> types) in pluginsWithDependencies)
        {
            var pluginTypes = _pluginsByAssembly[assembly].Concat(types.SelectMany(it => _pluginsByAssembly[it]));

            foreach (var pluginType in pluginTypes)
            {
                yield return PluginTreeNodeBuilder.BuildPluginDescriptor(services, pluginType);
            }
        }
    }

    private static IServiceCollection ConfigurePlugin<TPluginDescriptor>(IServiceCollection services)
        where TPluginDescriptor : IPluginDescriptor
        => TPluginDescriptor.ConfigureServices(services);

    /// <summary>
    /// Loads the available plugin assemblies.
    /// </summary>
    /// <param name="reload">If <see langword="true"/>, this will empty and re-create the plugins.</param>
    [MemberNotNull(nameof(_pluginsByAssembly))]
    private void LoadAvailablePluginAssemblies(bool reload = false)
    {
        if (!reload && _pluginsByAssembly?.Count > 0)
        {
            return;
        }

        var searchPaths = new List<string>();

        if (_options.ScanAssemblyDirectory)
        {
            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;

            if (entryAssemblyPath is not null)
            {
                var installationDirectory = Directory.GetParent(entryAssemblyPath)
                                            ?? throw new InvalidOperationException();

                searchPaths.Add(installationDirectory.FullName);
            }
        }

        searchPaths.AddRange(_options.PluginSearchPaths);

        var assemblyPaths = searchPaths.Select
        (
            searchPath => Directory.EnumerateFiles
            (
                searchPath,
                "*.dll",
                SearchOption.AllDirectories
            )
        )
        .SelectMany(a => a)
        .ToArray();

        _pluginsByAssembly = new(assemblyPaths.Length);
        foreach (var assemblyPath in assemblyPaths)
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(assemblyPath);
            }
            catch
            {
                continue;
            }

            if (assembly.GetCustomAttribute<RemoraPlugin>() is not null)
            {
                _pluginsByAssembly[assembly] = assembly.GetExportedTypes().Where(IsPlugin);
            }
        }

        static bool IsPlugin(Type type)
            => typeof(IPluginDescriptor).IsAssignableFrom(type) &&
               type is { IsAbstract: false, IsInterface: false };
    }
}
