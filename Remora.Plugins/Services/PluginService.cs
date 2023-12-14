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
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Remora.Plugins.Abstractions;
using Remora.Results;

namespace Remora.Plugins.Services;

/// <summary>
/// Serves functionality related to plugins.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PluginService"/> class.
/// </remarks>
/// <param name="serviceProvider">The service provider where plugins are stored.</param>
/// <param name="options">The service options.</param>
[PublicAPI]
public sealed class PluginService(IServiceProvider serviceProvider)
    : IAsyncDisposable, IDisposable
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    private readonly List<IPluginDescriptor> _plugins = new();

    /// <summary>
    /// Iterates through each plugin, initializes them, and executes <see cref="IPluginDescriptor.InitializeAsync(CancellationToken)"/>.
    /// </summary>
    /// <param name="ct">A cancellation token for this operation.</param>
    /// <returns>A set of results indicating success or failure of each initialize operation.</returns>
    public async ValueTask<IReadOnlyList<Result>> InitializePluginsAsync(CancellationToken ct = default)
    {
        var plugins = _serviceProvider.GetServices<IPluginDescriptor>();

        List<Result> results = new();

        foreach (var plugin in plugins)
        {
            var pluginResult = await plugin.InitializeAsync(ct);
            if (pluginResult.IsSuccess)
            {
                _plugins.Add(plugin);
            }
            results.Add(pluginResult);
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Iterates through initialized plugins and applies migrations where appropriate.
    /// </summary>
    /// <param name="ct">A cancellation token for this operation.</param>
    /// <returns>A set of results indicating success or failure of each initialize operation.</returns>
    public async ValueTask<IReadOnlyList<Result>> MigratePluginsAsync(CancellationToken ct = default)
    {
        List<Result> results = new();

        foreach (var plugin in _plugins)
        {
            if (typeof(IMigratablePlugin).IsAssignableFrom(plugin.GetType()))
            {
                results.Add(await (plugin as IMigratablePlugin)!.MigrateAsync(ct));
            }
        }
        return results.AsReadOnly();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }
}
