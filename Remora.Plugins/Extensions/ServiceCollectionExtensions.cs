//
//  ServiceCollectionExtensions.cs
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Remora.Plugins.Abstractions;
using Remora.Plugins.Abstractions.Attributes;
using Remora.Plugins.Services;

namespace Remora.Plugins.Extensions
{
    /// <summary>
    /// Provides a set of extensions for <see cref="IServiceCollection"/>.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Searches the defined locations for locations for assemblies annotated with <see cref="RemoraPluginAttribute"/>,
        /// finds types which implement <see cref="IPluginDescriptor"/>, and registers them with the service provider.
        /// </summary>
        /// <param name="services">The service collection to modify.</param>
        /// <param name="options">Options to define search locations.</param>
        /// <returns>The current <see cref="IServiceCollection"/>, for chaining.</returns>
        [PublicAPI]
        public static IServiceCollection AddPlugins(this IServiceCollection services, IOptions<PluginServiceOptions>? options = null)
        {
            var pluginOptions = options?.Value ?? PluginServiceOptions.Default;
            return AddPlugins(services, pluginOptions);
        }

        /// <summary>
        /// Searches the defined locations for locations for assemblies annotated with <see cref="RemoraPluginAttribute"/>,
        /// finds types which implement <see cref="IPluginDescriptor"/>, and registers them with the service provider.
        /// </summary>
        /// <param name="services">The service collection to modify.</param>
        /// <param name="options">Options to define search locations. Defaults to <see cref="PluginServiceOptions.Default"/>.</param>
        /// <returns>The current <see cref="IServiceCollection"/>, for chaining.</returns>
        [PublicAPI]
        public static IServiceCollection AddPlugins(this IServiceCollection services, PluginServiceOptions? options = null)
        {
            var pluginOptions = options ?? PluginServiceOptions.Default;
            IEnumerable<Assembly> pluginAssemblies = FindPluginAssemblies(pluginOptions);
            var pluginsWithDependencies = pluginAssemblies.ToDictionary
            (
                a => a,
                a => a.GetReferencedAssemblies()
                    .Where(ra => pluginAssemblies.Any(pa => pa.FullName == ra.FullName)) // Retrieve AssemblyNames where the Assembly is a Plugin Assembly.
                    .Select(ra => pluginAssemblies.First(pa => pa.FullName == ra.FullName)) // Get Assemblies based on the filtered names.
                    .Select(ra => ra) // Iterate assemblies
            );

            var sorted = pluginsWithDependencies.Keys.TopologicalSort(k => pluginsWithDependencies[k]).ToList();

            // For each assembly
            foreach (var current in sorted)
            {
                if (current is null)
                {
                    continue;
                }

                var configureHelper = typeof(ServiceCollectionExtensions).GetMethod(nameof(ConfigurePlugin));

                // This uses IsAssignableFrom because the .NET Standard target does not have IsAssignableTo().
                var plugins = current.GetExportedTypes().Where(it => typeof(IPluginDescriptor).IsAssignableFrom(it) && !it.IsAbstract && !it.IsInterface);

                // For each plugin in assembly
                foreach (var plugin in plugins)
                {
                    configureHelper!.MakeGenericMethod(plugin).Invoke(null, new[] { services });
                    services.TryAddSingleton(plugin);
                }
            }

            services.TryAddSingleton<PluginService>();

            return services;
        }

#if NET8_0_OR_GREATER
        private static IServiceCollection ConfigurePlugin<TPluginDescriptor>(IServiceCollection serviceCollection)
            where TPluginDescriptor : IPluginDescriptor
            => TPluginDescriptor.ConfigureServices(serviceCollection);
#else
#pragma warning disable SA1010 // Opening square brackets should be spaced correctly
        private static IServiceCollection ConfigurePlugin<TPluginDescriptor>(IServiceCollection serviceCollection)
            where TPluginDescriptor : IPluginDescriptor
        {
            const string ConfigureServicesMethodName = "ConfigureServices";

            var configure = typeof(TPluginDescriptor).GetMethod(ConfigureServicesMethodName, BindingFlags.Static | BindingFlags.Public, null, [typeof(IServiceCollection)], []);

            return configure is null
                ? serviceCollection
                : (IServiceCollection)configure.Invoke(null, [serviceCollection])!;
        }
#pragma warning restore SA1010 // Opening square brackets should be spaced correctly
#endif

        /// <summary>
        /// Finds available plugin assemblies.
        /// </summary>
        /// <returns>The available assemblies.</returns>
        private static IEnumerable<Assembly> FindPluginAssemblies(PluginServiceOptions options)
        {
            var searchPaths = new List<string>();

            if (options.ScanAssemblyDirectory)
            {
                var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;

                if (entryAssemblyPath is not null)
                {
                    var installationDirectory = Directory.GetParent(entryAssemblyPath)
                                                ?? throw new InvalidOperationException();

                    searchPaths.Add(installationDirectory.FullName);
                }
            }

            searchPaths.AddRange(options.PluginSearchPaths);

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
}
