// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Steam.Data;

public sealed class ItemAction {
	[JsonInclude]
	[JsonPropertyName("link")]
	[JsonRequired]
	[PublicAPI]
	public string Link { get; private init; } = "";

	[JsonInclude]
	[JsonPropertyName("name")]
	[JsonRequired]
	[PublicAPI]
	public string Name { get; private init; } = "";

	internal ItemAction(string link, string name) {
		ArgumentException.ThrowIfNullOrEmpty(link);
		ArgumentException.ThrowIfNullOrEmpty(name);

		Link = link;
		Name = name;
	}

	[JsonConstructor]
	private ItemAction() { }
}
