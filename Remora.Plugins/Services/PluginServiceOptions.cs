//
//  PluginServiceOptions.cs
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

using System.Collections.Generic;

namespace Remora.Plugins.Services;

/// <summary>
/// Represents various options made available to the plugin service.
/// </summary>
/// <param name="PluginSearchPaths">Additional plugin search paths to consider.</param>
/// <param name="ScanAssemblyDirectory">
/// Whether the directory of the entry assembly should be scanned for plugins.
/// </param>
public record PluginServiceOptions
(
    IEnumerable<string> PluginSearchPaths,
    bool ScanAssemblyDirectory = true
);
