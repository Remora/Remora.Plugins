//
//  PluginTreeBuilder.cs
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
using JetBrains.Annotations;
using Remora.Plugins.Abstractions;

namespace Remora.Plugins
{
    /// <summary>
    /// A type that facilitates the creation of a <see cref="PluginTree"/>.
    /// </summary>
    /// <param name="filter">A filter predicate which determines if the plugin should be loaded.</param>
    [PublicAPI]
    public sealed class PluginTreeBuilder(Predicate<IPluginDescriptor> filter)
    {
        private readonly List<PluginTreeNodeBuilder> _treeNodeBuilders = [];

        /// <summary>
        /// Adds a tree node builder to the collection.
        /// </summary>
        /// <param name="node">The node to add.</param>
        public void AddTreeNode(PluginTreeNodeBuilder node)
        {
            _treeNodeBuilders.Add(node);
        }

        /// <summary>
        /// Builds the <see cref="PluginTree"/>.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <returns>A new <see cref="PluginTree"/>.</returns>
        [Pure]
        [PublicAPI]
        public PluginTree Build(IServiceProvider serviceProvider)
        {
            var tree = new PluginTree();
            foreach (var node in _treeNodeBuilders)
            {
                var pluginTreeNode = node.Build(serviceProvider);
                if (filter.Invoke(pluginTreeNode.Plugin))
                {
                    tree.AddBranch(pluginTreeNode);
                }
            }
            return tree;
        }
    }
}
