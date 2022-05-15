//
//  PluginTree.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2017 Jarl Gullberg
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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Remora.Plugins.Abstractions;
using Remora.Plugins.Errors;
using Remora.Results;

namespace Remora.Plugins;

/// <summary>
/// Represents a tree of plugins, ordered by their dependencies.
/// </summary>
[PublicAPI]
public sealed class PluginTree
{
    private readonly List<PluginTreeNode> _branches;

    /// <summary>
    /// Gets the root nodes of the identified plugin dependency branches. The root node is considered to be the
    /// application itself, which is implicitly initialized.
    /// </summary>
    public IReadOnlyCollection<PluginTreeNode> Branches => _branches;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginTree"/> class.
    /// </summary>
    /// <param name="branches">The dependency branches.</param>
    public PluginTree(List<PluginTreeNode>? branches = null)
    {
        _branches = branches ?? new List<PluginTreeNode>();
    }

    /// <summary>
    /// Configures the services required by the plugins.
    /// </summary>
    /// <param name="serviceCollection">The service collection to configure.</param>
    /// <returns>A result which may or may not have succeeded.</returns>
    public Result ConfigureServices(IServiceCollection serviceCollection)
    {
        var results = Walk
        (
            node => new PluginConfigurationFailed
            (
                node.Plugin,
                "One or more of the plugin's dependencies failed to configure their services."
            ),
            node => node.Plugin.ConfigureServices(serviceCollection)
        ).ToList();

        return results.Any(r => !r.IsSuccess)
            ? new AggregateError(results.Where(r => !r.IsSuccess).Cast<IResult>().ToList())
            : Result.FromSuccess();
    }

    /// <summary>
    /// Initializes the plugins in the tree.
    /// </summary>
    /// <param name="services">The available services.</param>
    /// <param name="ct">The cancellation token for this operation.</param>
    /// <returns>A result which may or may not have succeeded.</returns>
    public async Task<Result> InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var results = await WalkAsync
        (
            node => new PluginInitializationFailed
            (
                node.Plugin,
                "One or more of the plugin's dependencies failed to initialize."
            ),
            async (node, c) => await node.Plugin.InitializeAsync(services, c),
            ct: ct
        ).ToListAsync(ct);

        return results.Any(r => !r.IsSuccess)
            ? new AggregateError(results.Where(r => !r.IsSuccess).Cast<IResult>().ToList())
            : Result.FromSuccess();
    }

    /// <summary>
    /// Migrates any persistent data stores of the plugins in the tree.
    /// </summary>
    /// <param name="services">The available services.</param>
    /// <param name="ct">The cancellation token for this operation.</param>
    /// <returns>A result which may or may not have succeeded.</returns>
    public async Task<Result> MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var results = await WalkAsync
        (
            node => new PluginMigrationFailed
            (
                node.Plugin,
                "One or more of the plugin's dependencies failed to migrate."
            ),
            async (node, c) =>
            {
                if (node.Plugin is not IMigratablePlugin migratablePlugin)
                {
                    return Result.FromSuccess();
                }

                return await migratablePlugin.MigrateAsync(services, c);
            },
            ct: ct
        ).ToListAsync(ct);

        return results.Any(r => !r.IsSuccess)
            ? new AggregateError(results.Where(r => !r.IsSuccess).Cast<IResult>().ToList())
            : Result.FromSuccess();
    }

    /// <summary>
    /// Asynchronously walks the plugin tree, performing the given operations on each node. If the operation fails,
    /// the walk terminates at that point. If a node appears in more than one place in the tree, the operations are only
    /// performed the first time it is encountered, and the walk will not proceed down any child paths the node may
    /// have.
    /// </summary>
    /// <param name="errorFactory">
    /// A factory function to create an error when the operation fails on the parent node.
    /// </param>
    /// <param name="preOperation">The operation to perform while walking down into the tree.</param>
    /// <param name="postOperation">The operation to perform while walking up into the tree.</param>
    /// <param name="ct">The cancellation token for this operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async IAsyncEnumerable<Result> WalkAsync
    (
        Func<PluginTreeNode, Result> errorFactory,
        Func<PluginTreeNode, CancellationToken, Task<Result>> preOperation,
        Func<PluginTreeNode, CancellationToken, Task<Result>>? postOperation = null,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        var visitedNodes = new HashSet<PluginTreeNode>();
        foreach (var branch in _branches)
        {
            await foreach
            (
                var nodeResult in WalkNodeAsync(branch, visitedNodes, errorFactory, preOperation, postOperation, ct)
            )
            {
                yield return nodeResult;
            }
        }
    }

    /// <summary>
    /// Walks the plugin tree, performing the given operations on each node. If the operation fails, the walk
    /// terminates at that point.
    /// </summary>
    /// <param name="errorFactory">
    /// A factory function to create an error when the operation fails on the parent node.
    /// </param>
    /// <param name="preOperation">The operation to perform while walking down into the tree.</param>
    /// <param name="postOperation">The operation to perform while walking up into the tree.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public IEnumerable<Result> Walk
    (
        Func<PluginTreeNode, Result> errorFactory,
        Func<PluginTreeNode, Result> preOperation,
        Func<PluginTreeNode, Result>? postOperation = null
    )
    {
        var visitedNodes = new HashSet<PluginTreeNode>();
        return _branches.SelectMany
        (
            branch => WalkNode(branch, visitedNodes, errorFactory, preOperation, postOperation)
        );
    }

    /// <summary>
    /// Adds a dependency branch to the tree.
    /// </summary>
    /// <param name="branch">The branch.</param>
    internal void AddBranch(PluginTreeNode branch)
    {
        if (_branches.Contains(branch))
        {
            return;
        }

        _branches.Add(branch);
    }

    private async IAsyncEnumerable<Result> WalkNodeAsync
    (
        PluginTreeNode node,
        ISet<PluginTreeNode> visitedNodes,
        Func<PluginTreeNode, Result> errorFactory,
        Func<PluginTreeNode, CancellationToken, Task<Result>> preOperation,
        Func<PluginTreeNode, CancellationToken, Task<Result>>? postOperation = null,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        if (visitedNodes.Contains(node))
        {
            // No need to traverse this again
            yield break;
        }

        visitedNodes.Add(node);

        var shouldTerminate = false;

        await foreach (var p in PerformNodeOperationAsync(node, errorFactory, preOperation, ct))
        {
            yield return p;
            if (!p.IsSuccess)
            {
                shouldTerminate = true;
            }
        }

        foreach (var dependent in node.Dependents)
        {
            await foreach
            (
                var result in WalkNodeAsync(dependent, visitedNodes, errorFactory, preOperation, postOperation)
            )
            {
                if (!result.IsSuccess)
                {
                    shouldTerminate = true;
                }

                yield return result;
            }
        }

        if (postOperation is null || shouldTerminate)
        {
            yield break;
        }

        await foreach (var p in PerformNodeOperationAsync(node, errorFactory, postOperation, ct))
        {
            yield return p;
        }
    }

    private IEnumerable<Result> WalkNode
    (
        PluginTreeNode node,
        ISet<PluginTreeNode> visitedNodes,
        Func<PluginTreeNode, Result> errorFactory,
        Func<PluginTreeNode, Result> preOperation,
        Func<PluginTreeNode, Result>? postOperation = null
    )
    {
        if (visitedNodes.Contains(node))
        {
            // No need to traverse this again
            yield break;
        }

        visitedNodes.Add(node);

        var shouldTerminate = false;

        foreach (var p in PerformNodeOperation(node, errorFactory, preOperation))
        {
            yield return p;
            if (!p.IsSuccess)
            {
                shouldTerminate = true;
            }
        }

        foreach (var dependent in node.Dependents)
        {
            foreach (var result in WalkNode(dependent, visitedNodes, errorFactory, preOperation, postOperation))
            {
                if (!result.IsSuccess)
                {
                    shouldTerminate = true;
                }

                yield return result;
            }
        }

        if (postOperation is null || shouldTerminate)
        {
            yield break;
        }

        foreach (var p in PerformNodeOperation(node, errorFactory, postOperation))
        {
            yield return p;
        }
    }

    private static async IAsyncEnumerable<Result> PerformNodeOperationAsync
    (
        PluginTreeNode node,
        Func<PluginTreeNode, Result> errorFactory,
        Func<PluginTreeNode, CancellationToken, Task<Result>> operation,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        Result operationResult;
        try
        {
            operationResult = await operation(node, ct);
        }
        catch (Exception e)
        {
            operationResult = e;
        }

        yield return operationResult;
        if (operationResult.IsSuccess)
        {
            yield break;
        }

        foreach (var dependent in node.GetAllDependents())
        {
            yield return errorFactory(dependent);
        }
    }

    private static IEnumerable<Result> PerformNodeOperation
    (
        PluginTreeNode node,
        Func<PluginTreeNode, Result> errorFactory,
        Func<PluginTreeNode, Result> operation
    )
    {
        Result operationResult;
        try
        {
            operationResult = operation(node);
        }
        catch (Exception e)
        {
            operationResult = e;
        }

        yield return operationResult;
        if (operationResult.IsSuccess)
        {
            yield break;
        }

        foreach (var dependent in node.GetAllDependents())
        {
            yield return errorFactory(dependent);
        }
    }
}
