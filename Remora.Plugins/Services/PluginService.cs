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
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Remora.Plugins.Abstractions;
using Remora.Plugins.Abstractions.Attributes;
using Remora.Plugins.Extensions;

namespace Remora.Plugins.Services;

/// <summary>
/// Serves functionality related to plugins.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PluginService"/> class.
/// </remarks>
/// <param name="options">The service options.</param>
[PublicAPI]
public sealed class PluginService(IOptions<PluginServiceOptions>? options = null)
{
    private readonly PluginServiceOptions _options = options?.Value ?? PluginServiceOptions.Default;

    /// <summary>
    /// Loads all available plugins into a tree structure, ordered by their topological dependencies. Effectively, this
    /// means that <see cref="PluginTree.Branches"/> will contain dependency-free plugins, with subsequent
    /// dependents below them (recursively).
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> used to register services.</param>
    /// <returns>The dependency tree.</returns>
    [PublicAPI, Pure]
    public PluginTree LoadPluginTree(IServiceCollection services)
    {
        bool IsDirectDependency(Assembly assembly, Assembly dependency)
        {
            var dependencies = pluginsWithDependencies[assembly];
            return IsDependency(assembly, dependency) && dependencies.All(d => !IsDependency(d, dependency));
        }

        var loadDescriptorResult = LoadPluginDescriptor(current);
        if (!loadDescriptorResult.IsSuccess)
        {
            continue;
        }

        var node = new PluginTreeNode(loadDescriptorResult.Entity);
        var dependencies = pluginsWithDependencies[current].ToList();
        if (!dependencies.Any())
        {
            // This is a root of a chain
            tree.AddBranch(node);
        }

        foreach (var dependency in dependencies)
        {
            if (!IsDirectDependency(current, dependency))
            {
                continue;
            }

            var dependencyNode = nodes[dependency];
            dependencyNode.AddDependent(node);
        }

        nodes.Add(current, node);
        sorted.Remove(current);

        return tree;
    }

    /// <summary>
    /// Loads all available plugins into a flat list.
    /// </summary>
    /// <remarks>
    /// This method should generally not be used for actually loading plugins into your application, since it may not
    /// properly order plugins in more complex dependency graphs. Prefer using <see cref="LoadPluginTree"/> and its
    /// associated methods.
    /// </remarks>
    /// <returns>The descriptors of the available plugins.</returns>
    [Pure]
    public IEnumerable<IPluginDescriptor> LoadPlugins()
    {
        var pluginAssemblies = LoadAvailablePluginAssemblies().ToList();
        var sorted = pluginAssemblies.TopologicalSort
        (
            a => a.PluginAssembly.GetReferencedAssemblies()
                .Where
                (
                    n => pluginAssemblies.Any(pa => pa.PluginAssembly.GetName().FullName == n.FullName)
                )
                .Select
                (
                    n => pluginAssemblies.First(pa => pa.PluginAssembly.GetName().FullName == n.FullName)
                )
        );

        foreach (var pluginAssembly in sorted)
        {
            var descriptor = (IPluginDescriptor?)Activator.CreateInstance
            (
                pluginAssembly.PluginAttribute.PluginDescriptor
            );

            if (descriptor is null)
            {
                continue;
            }

            yield return descriptor;
        }
    }

    /// <summary>
    /// Finds available plugin assemblies.
    /// </summary>
    /// <returns>The available assemblies.</returns>
    [Pure]
    private IEnumerable<Assembly> FindPluginAssemblies()
    {
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
        ).SelectMany(a => a);

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

            var pluginAttribute = assembly.GetCustomAttribute<RemoraPluginAttribute>();
            if (pluginAttribute is null)
            {
                continue;
            }

            yield return assembly;
        }
    }
}
