//
//  PluginTreeNodeBuilder.cs
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
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Remora.Plugins.Abstractions;

namespace Remora.Plugins
{
    /// <summary>
    /// A type which stages the creation of a <see cref="PluginTreeNode"/>.
    /// </summary>
    /// <param name="pluginAssembly">The assembly the plugin belongs to.</param>
    /// <param name="pluginType">The type of the plugin to construct.</param>
    [PublicAPI]
    public sealed class PluginTreeNodeBuilder(Assembly pluginAssembly, Type pluginType)
    {
        /// <summary>
        /// Gets the plugin type.
        /// </summary>
        [PublicAPI]
        public Type PluginType { get; } = pluginType;

        /// <summary>
        /// Gets the plugin assembly.
        /// </summary>
        [PublicAPI]
        public Assembly PluginAssembly { get; } = pluginAssembly;

        /// <summary>
        /// Gets the dependents of this plugin node.
        /// </summary>
        [PublicAPI]
        public List<PluginTreeNodeBuilder> Dependents { get; } = [];

        /// <summary>
        /// Adds a dependent.
        /// </summary>
        /// <param name="dependent">The dependent to add.</param>
        public void AddDependent(PluginTreeNodeBuilder dependent)
        {
            this.Dependents.Add(dependent);
        }

        /// <summary>
        /// Builds the plugin tree node.
        /// </summary>
        /// <param name="services">The service provider used to construct the plugins.</param>
        /// <returns>A newly constructed <see cref="PluginTreeNode"/>.</returns>
        [Pure]
        public PluginTreeNode Build(IServiceProvider services)
        {
            var plugin = BuildPluginDescriptor(services, this.PluginType);
            var node = new PluginTreeNode(plugin);

            foreach (var dependent in this.Dependents)
            {
                var dependentPlugin = BuildPluginDescriptor(services, dependent.PluginType);
                var dependentNode = new PluginTreeNode(dependentPlugin);
                node.AddDependent(dependentNode);
            }

            return node;
        }

        /// <summary>
        /// Builds a <see cref="IPluginDescriptor"/> from the provided <paramref name="services"/>.
        /// </summary>
        /// <param name="services">The service provider.</param>
        /// <param name="pluginType">The plugin type.</param>
        /// <returns>The newly constructed <see cref="IPluginDescriptor"/>.</returns>
        [Pure]
        internal static IPluginDescriptor BuildPluginDescriptor(IServiceProvider services, Type pluginType)
            => (IPluginDescriptor)ActivatorUtilities.CreateInstance(services, pluginType);
    }
}
