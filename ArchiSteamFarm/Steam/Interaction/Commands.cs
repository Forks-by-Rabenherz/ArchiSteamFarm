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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Steam.Cards;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;
using SteamKit2;
using SteamKit2.Internal;
using SteamKit2.WebUI.Internal;

namespace ArchiSteamFarm.Steam.Interaction;

public sealed class Commands {
	private const ushort SteamTypingStatusDelay = 10 * 1000; // Steam client broadcasts typing status each 10 seconds

	private readonly Bot Bot;
	private readonly Dictionary<uint, string> CachedGamesOwned = new();

	internal Commands(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Bot = bot;
	}

	[PublicAPI]
	public static string FormatBotResponse(string response, string botName) {
		ArgumentException.ThrowIfNullOrEmpty(response);
		ArgumentException.ThrowIfNullOrEmpty(botName);

		return $"{Environment.NewLine}<{botName}> {response}";
	}

	[PublicAPI]
	public string FormatBotResponse(string response) {
		ArgumentException.ThrowIfNullOrEmpty(response);

		return $"<{Bot.BotName}> {response}";
	}

	[PublicAPI]
	public static string FormatStaticResponse(string response) {
		ArgumentException.ThrowIfNullOrEmpty(response);

		return $"<{SharedInfo.ASF}> {response}";
	}

	[PublicAPI]
	public static EAccess GetProxyAccess(Bot bot, EAccess access, ulong steamID = 0) {
		// The objective here should be simple, calculating effective access of the user
		// Problem is, history already proved nothing in this damn file is as simple as it seems
		// We use this function for proxying commands such as !status 2 sent to bot 1, which should use 2's user access instead
		ArgumentNullException.ThrowIfNull(bot);

		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		// If we got executed with owner access or lack steamID entirely, then this is effective access
		if ((access >= EAccess.Owner) || (steamID == 0)) {
			return access;
		}

		// Otherwise, effective access is the access of the user on target bot, whatever that would be, not this one
		return bot.GetAccess(steamID);
	}

	[PublicAPI]
	public async Task<string?> Response(EAccess access, string message, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(message);

		string[] args = message.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);

		switch (args.Length) {
			case 0:
				throw new InvalidOperationException(nameof(args.Length));
			case 1:
				switch (args[0].ToUpperInvariant()) {
					case "2FA":
						return await Response2FA(access).ConfigureAwait(false);
					case "2FANO":
						return await Response2FAConfirm(access, false).ConfigureAwait(false);
					case "2FAOK":
						return await Response2FAConfirm(access, true).ConfigureAwait(false);
					case "BALANCE":
						return ResponseWalletBalance(access);
					case "BGR":
						return ResponseBackgroundGamesRedeemer(access);
					case "BGRCLEAR":
						return ResponseBackgroundGamesRedeemerClear(access);
					case "EXIT":
						return ResponseExit(access);
					case "FARM":
						return await ResponseFarm(access).ConfigureAwait(false);
					case "FB":
						return ResponseFarmingBlacklist(access);
					case "FQ":
						return ResponseFarmingQueue(access);
					case "HELP":
						return ResponseHelp(access);
					case "INVENTORY":
						return await ResponseInventory(access).ConfigureAwait(false);
					case "LEVEL":
						return await ResponseLevel(access).ConfigureAwait(false);
					case "LOOT":
						return await ResponseLoot(access).ConfigureAwait(false);
					case "MAB":
						return ResponseMatchActivelyBlacklist(access);
					case "PAUSE":
						return await ResponsePause(access, true).ConfigureAwait(false);
					case "PAUSE~":
						return await ResponsePause(access, false).ConfigureAwait(false);
					case "POINTS":
						return await ResponsePointsBalance(access).ConfigureAwait(false);
					case "RESET":
						return await ResponseReset(access).ConfigureAwait(false);
					case "RESUME":
						return ResponseResume(access);
					case "RESTART":
						return ResponseRestart(access);
					case "SA":
						return await ResponseStatus(access, SharedInfo.ASF, steamID).ConfigureAwait(false);
					case "START":
						return ResponseStart(access);
					case "STATS":
						return ResponseStats(access);
					case "STATUS":
						return ResponseStatus(access).Response;
					case "STOP":
						return await ResponseStop(access).ConfigureAwait(false);
					case "TB":
						return ResponseTradingBlacklist(access);
					case "UNPACK":
						return await ResponseUnpackBoosters(access).ConfigureAwait(false);
					case "UPDATE":
						return await ResponseUpdate(access).ConfigureAwait(false);
					case "UPDATEPLUGINS":
						return await ResponseUpdatePlugins(access).ConfigureAwait(false);
					case "VERSION":
						return ResponseVersion(access);
					default:
						string? pluginsResponse = await PluginsCore.OnBotCommand(Bot, access, message, args, steamID).ConfigureAwait(false);

						return !string.IsNullOrEmpty(pluginsResponse) ? pluginsResponse : ResponseUnknown(access);
				}
			default:
				switch (args[0].ToUpperInvariant()) {
					case "2FA":
						return await Response2FA(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "2FANO":
						return await Response2FAConfirm(access, Utilities.GetArgsAsText(args, 1, ","), false, steamID).ConfigureAwait(false);
					case "2FAOK":
						return await Response2FAConfirm(access, Utilities.GetArgsAsText(args, 1, ","), true, steamID).ConfigureAwait(false);
					case "AL" or "ADDLICENCE" or "ADDLICENSE" when args.Length > 2:
						return await ResponseAddLicense(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "AL" or "ADDLICENCE" or "ADDLICENSE":
						return await ResponseAddLicense(access, args[1]).ConfigureAwait(false);
					case "ALA":
						return await ResponseAddLicense(access, SharedInfo.ASF, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "BALANCE":
						return await ResponseWalletBalance(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "BGR":
						return await ResponseBackgroundGamesRedeemer(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "BGRCLEAR":
						return await ResponseBackgroundGamesRedeemerClear(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "ENCRYPT" when args.Length > 2:
						return ResponseEncrypt(access, args[1], Utilities.GetArgsAsText(message, 2));
					case "FARM":
						return await ResponseFarm(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "FB":
						return await ResponseFarmingBlacklist(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "FBADD" when args.Length > 2:
						return await ResponseFarmingBlacklistAdd(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "FBADD":
						return ResponseFarmingBlacklistAdd(access, args[1]);
					case "FBRM" when args.Length > 2:
						return await ResponseFarmingBlacklistRemove(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "FBRM":
						return ResponseFarmingBlacklistRemove(access, args[1]);
					case "FQ":
						return await ResponseFarmingQueue(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "FQADD" when args.Length > 2:
						return await ResponseFarmingQueueAdd(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "FQADD":
						return ResponseFarmingQueueAdd(access, args[1]);
					case "FQRM" when args.Length > 2:
						return await ResponseFarmingQueueRemove(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "FQRM":
						return ResponseFarmingQueueRemove(access, args[1]);
					case "HASH" when args.Length > 2:
						return ResponseHash(access, args[1], Utilities.GetArgsAsText(message, 2));
					case "INPUT" when args.Length > 3:
						return await ResponseInput(access, args[1], args[2], Utilities.GetArgsAsText(message, 3), steamID).ConfigureAwait(false);
					case "INPUT" when args.Length > 2:
						return ResponseInput(access, args[1], args[2]);
					case "INVENTORY":
						return await ResponseInventory(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "LEVEL":
						return await ResponseLevel(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "LOOT":
						return await ResponseLoot(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "LOOT^" when args.Length > 3:
						return await ResponseAdvancedLoot(access, args[1], args[2], Utilities.GetArgsAsText(message, 3), steamID).ConfigureAwait(false);
					case "LOOT^" when args.Length > 2:
						return await ResponseAdvancedLoot(access, args[1], args[2]).ConfigureAwait(false);
					case "LOOT&" when args.Length > 4:
						return await ResponseAdvancedLootByAssetRarity(access, args[1], args[2], args[3], Utilities.GetArgsAsText(args, 4, ",")).ConfigureAwait(false);
					case "LOOT&" when args.Length > 3:
						return await ResponseAdvancedLootByAssetRarity(access, args[1], args[2], args[3]).ConfigureAwait(false);
					case "LOOT@" when args.Length > 2:
						return await ResponseLootByRealAppIDs(access, args[1], Utilities.GetArgsAsText(args, 2, ","), false, steamID).ConfigureAwait(false);
					case "LOOT@":
						return await ResponseLootByRealAppIDs(access, args[1], false).ConfigureAwait(false);
					case "LOOT%" when args.Length > 2:
						return await ResponseLootByRealAppIDs(access, args[1], Utilities.GetArgsAsText(args, 2, ","), true, steamID).ConfigureAwait(false);
					case "LOOT%":
						return await ResponseLootByRealAppIDs(access, args[1], true).ConfigureAwait(false);
					case "MAB":
						return await ResponseMatchActivelyBlacklist(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "MABADD" when args.Length > 2:
						return await ResponseMatchActivelyBlacklistAdd(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "MABADD":
						return ResponseMatchActivelyBlacklistAdd(access, args[1]);
					case "MABRM" when args.Length > 2:
						return await ResponseMatchActivelyBlacklistRemove(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "MABRM":
						return ResponseMatchActivelyBlacklistRemove(access, args[1]);
					case "NICKNAME" when args.Length > 2:
						return await ResponseNickname(access, args[1], Utilities.GetArgsAsText(message, 2), steamID).ConfigureAwait(false);
					case "NICKNAME":
						return ResponseNickname(access, args[1]);
					case "OA":
						return await ResponseOwns(access, SharedInfo.ASF, Utilities.GetArgsAsText(message, 1), steamID).ConfigureAwait(false);
					case "OWNS" when args.Length > 2:
						return await ResponseOwns(access, args[1], Utilities.GetArgsAsText(message, 2), steamID).ConfigureAwait(false);
					case "OWNS":
						return (await ResponseOwns(access, args[1]).ConfigureAwait(false)).Response;
					case "PAUSE":
						return await ResponsePause(access, Utilities.GetArgsAsText(args, 1, ","), true, steamID: steamID).ConfigureAwait(false);
					case "PAUSE~":
						return await ResponsePause(access, Utilities.GetArgsAsText(args, 1, ","), false, steamID: steamID).ConfigureAwait(false);
					case "PAUSE&" when args.Length > 2:
						return await ResponsePause(access, args[1], true, Utilities.GetArgsAsText(message, 2), steamID).ConfigureAwait(false);
					case "PAUSE&":
						return await ResponsePause(access, true, args[1]).ConfigureAwait(false);
					case "PLAY" when args.Length > 2:
						return await ResponsePlay(access, args[1], Utilities.GetArgsAsText(message, 2), steamID).ConfigureAwait(false);
					case "PLAY":
						return await ResponsePlay(access, args[1]).ConfigureAwait(false);
					case "POINTS":
						return await ResponsePointsBalance(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "PRIVACY" when args.Length > 2:
						return await ResponsePrivacy(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "PRIVACY":
						return await ResponsePrivacy(access, args[1]).ConfigureAwait(false);
					case "R" or "REDEEM" when args.Length > 2:
						return await ResponseRedeem(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID: steamID).ConfigureAwait(false);
					case "R" or "REDEEM":
						return await ResponseRedeem(access, args[1], steamID).ConfigureAwait(false);
					case "R^" or "REDEEM^" when args.Length > 3:
						return await ResponseAdvancedRedeem(access, args[1], args[2], Utilities.GetArgsAsText(args, 3, ","), steamID).ConfigureAwait(false);
					case "R^" or "REDEEM^" when args.Length > 2:
						return await ResponseAdvancedRedeem(access, args[1], args[2], steamID).ConfigureAwait(false);
					case "RL" or "RMLICENCE" or "RMLICENSE" when args.Length > 2:
						return await ResponseRemoveLicense(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "RL" or "RMLICENCE" or "RMLICENSE":
						return await ResponseRemoveLicense(access, args[1]).ConfigureAwait(false);
					case "RP" or "REDEEMPOINTS" when args.Length > 2:
						return await ResponseRedeemPoints(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "RP" or "REDEEMPOINTS":
						return await ResponseRedeemPoints(access, args[1]).ConfigureAwait(false);
					case "RESET":
						return await ResponseReset(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "RESUME":
						return await ResponseResume(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "START":
						return await ResponseStart(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "STATUS":
						return await ResponseStatus(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "STOP":
						return await ResponseStop(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "TB":
						return await ResponseTradingBlacklist(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "TBADD" when args.Length > 2:
						return await ResponseTradingBlacklistAdd(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "TBADD":
						return ResponseTradingBlacklistAdd(access, args[1]);
					case "TBRM" when args.Length > 2:
						return await ResponseTradingBlacklistRemove(access, args[1], Utilities.GetArgsAsText(args, 2, ","), steamID).ConfigureAwait(false);
					case "TBRM":
						return ResponseTradingBlacklistRemove(access, args[1]);
					case "TRANSFER" when args.Length > 2:
						return await ResponseTransfer(access, args[1], Utilities.GetArgsAsText(message, 2), steamID).ConfigureAwait(false);
					case "TRANSFER":
						return await ResponseTransfer(access, args[1]).ConfigureAwait(false);
					case "TRANSFER^" when args.Length > 4:
						return await ResponseAdvancedTransfer(access, args[1], args[2], args[3], Utilities.GetArgsAsText(message, 4), steamID).ConfigureAwait(false);
					case "TRANSFER^" when args.Length > 3:
						return await ResponseAdvancedTransfer(access, args[1], args[2], args[3]).ConfigureAwait(false);
					case "TRANSFER&" when args.Length > 5:
						return await ResponseAdvancedTransferByAssetRarity(access, args[1], args[2], args[3], args[4], Utilities.GetArgsAsText(args, 5, ","), steamID).ConfigureAwait(false);
					case "TRANSFER&" when args.Length > 4:
						return await ResponseAdvancedTransferByAssetRarity(access, args[1], args[2], args[3], args[4]).ConfigureAwait(false);
					case "TRANSFER@" when args.Length > 3:
						return await ResponseTransferByRealAppIDs(access, args[1], args[2], Utilities.GetArgsAsText(message, 3), false, steamID).ConfigureAwait(false);
					case "TRANSFER@" when args.Length > 2:
						return await ResponseTransferByRealAppIDs(access, args[1], args[2], false).ConfigureAwait(false);
					case "TRANSFER%" when args.Length > 3:
						return await ResponseTransferByRealAppIDs(access, args[1], args[2], Utilities.GetArgsAsText(message, 3), true, steamID).ConfigureAwait(false);
					case "TRANSFER%" when args.Length > 2:
						return await ResponseTransferByRealAppIDs(access, args[1], args[2], true).ConfigureAwait(false);
					case "UNPACK":
						return await ResponseUnpackBoosters(access, Utilities.GetArgsAsText(args, 1, ","), steamID).ConfigureAwait(false);
					case "UPDATE":
						return await ResponseUpdate(access, args[1]).ConfigureAwait(false);
					case "UPDATEPLUGINS" when args.Length > 2:
						return await ResponseUpdatePlugins(access, args[1], Utilities.GetArgsAsText(args, 2, ",")).ConfigureAwait(false);
					case "UPDATEPLUGINS":
						return await ResponseUpdatePlugins(access, args[1]).ConfigureAwait(false);
					default:
						string? pluginsResponse = await PluginsCore.OnBotCommand(Bot, access, message, args, steamID).ConfigureAwait(false);

						return !string.IsNullOrEmpty(pluginsResponse) ? pluginsResponse : ResponseUnknown(access);
				}
		}
	}

	internal async Task HandleMessage(ulong steamID, string message) {
		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(message);

		string? commandPrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.CommandPrefix : GlobalConfig.DefaultCommandPrefix;

		if (!string.IsNullOrEmpty(commandPrefix)) {
			if (!message.StartsWith(commandPrefix, StringComparison.Ordinal)) {
				string? pluginsResponse = await PluginsCore.OnBotMessage(Bot, steamID, message).ConfigureAwait(false);

				if (!string.IsNullOrEmpty(pluginsResponse)) {
					if (!await Bot.SendMessage(steamID, pluginsResponse).ConfigureAwait(false)) {
						Bot.ArchiLogger.LogGenericWarning(Strings.FormatWarningFailedWithError(nameof(Bot.SendMessage)));
						Bot.ArchiLogger.LogGenericDebug(Strings.FormatContent(pluginsResponse));
					}
				}

				return;
			}

			if (message.Length == commandPrefix.Length) {
				// If the message starts with command prefix and is of the same length as command prefix, then it's just empty command trigger, useless
				return;
			}

			message = message[commandPrefix.Length..];
		}

		EAccess access = Bot.GetAccess(steamID);

		Task<string?> responseTask = Response(access, message, steamID);

		bool feedback = access >= EAccess.FamilySharing;

		if (feedback && !responseTask.IsCompleted) {
			if (!await Bot.SendTypingMessage(steamID).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.FormatWarningFailedWithError(nameof(Bot.SendTypingMessage)));
			}

			while (!responseTask.IsCompleted && (await Task.WhenAny(responseTask, Task.Delay(SteamTypingStatusDelay)).ConfigureAwait(false) != responseTask)) {
				if (!await Bot.SendTypingMessage(steamID).ConfigureAwait(false)) {
					Bot.ArchiLogger.LogGenericWarning(Strings.FormatWarningFailedWithError(nameof(Bot.SendTypingMessage)));
				}
			}
		}

		string? response = await responseTask.ConfigureAwait(false);

		if (string.IsNullOrEmpty(response)) {
			if (!feedback) {
				return;
			}

			response = FormatBotResponse(Strings.ErrorAccessDenied);
		}

		if (!await Bot.SendMessage(steamID, response).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericWarning(Strings.FormatWarningFailedWithError(nameof(Bot.SendMessage)));
			Bot.ArchiLogger.LogGenericDebug(Strings.FormatContent(response));
		}
	}

	internal async Task HandleMessage(ulong chatGroupID, ulong chatID, ulong steamID, string message) {
		ArgumentOutOfRangeException.ThrowIfZero(chatGroupID);
		ArgumentOutOfRangeException.ThrowIfZero(chatID);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(message);

		string? commandPrefix = ASF.GlobalConfig != null ? ASF.GlobalConfig.CommandPrefix : GlobalConfig.DefaultCommandPrefix;

		if (!string.IsNullOrEmpty(commandPrefix)) {
			if (!message.StartsWith(commandPrefix, StringComparison.Ordinal)) {
				string? pluginsResponse = await PluginsCore.OnBotMessage(Bot, steamID, message).ConfigureAwait(false);

				if (!string.IsNullOrEmpty(pluginsResponse)) {
					if (!await Bot.SendMessage(chatGroupID, chatID, pluginsResponse).ConfigureAwait(false)) {
						Bot.ArchiLogger.LogGenericWarning(Strings.FormatWarningFailedWithError(nameof(Bot.SendMessage)));
						Bot.ArchiLogger.LogGenericDebug(Strings.FormatContent(pluginsResponse));
					}
				}

				return;
			}

			if (message.Length == commandPrefix.Length) {
				// If the message starts with command prefix and is of the same length as command prefix, then it's just empty command trigger, useless
				return;
			}

			message = message[commandPrefix.Length..];
		}

		EAccess access = Bot.GetAccess(steamID);

		Task<string?> responseTask = Response(access, message, steamID);

		bool feedback = access >= EAccess.FamilySharing;

		if (feedback && !responseTask.IsCompleted) {
			string pleaseWaitMessage = FormatBotResponse(Strings.PleaseWait);

			if (!await Bot.SendMessage(chatGroupID, chatID, pleaseWaitMessage).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.FormatWarningFailedWithError(nameof(Bot.SendMessage)));
			}

			while (!responseTask.IsCompleted && (await Task.WhenAny(responseTask, Task.Delay(SteamTypingStatusDelay)).ConfigureAwait(false) != responseTask)) {
				if (!await Bot.SendMessage(chatGroupID, chatID, pleaseWaitMessage).ConfigureAwait(false)) {
					Bot.ArchiLogger.LogGenericWarning(Strings.FormatWarningFailedWithError(nameof(Bot.SendMessage)));
				}
			}
		}

		string? response = await responseTask.ConfigureAwait(false);

		if (string.IsNullOrEmpty(response)) {
			if (!feedback) {
				return;
			}

			response = FormatBotResponse(Strings.ErrorAccessDenied);
		}

		if (!await Bot.SendMessage(chatGroupID, chatID, response).ConfigureAwait(false)) {
			Bot.ArchiLogger.LogGenericWarning(Strings.FormatWarningFailedWithError(nameof(Bot.SendMessage)));
			Bot.ArchiLogger.LogGenericDebug(Strings.FormatContent(response));
		}
	}

	internal void OnNewLicenseList() {
		lock (CachedGamesOwned) {
			CachedGamesOwned.Clear();
			CachedGamesOwned.TrimExcess();
		}
	}

	private async Task<Dictionary<uint, string>?> FetchGamesOwned(bool cachedOnly = false) {
		lock (CachedGamesOwned) {
			if (CachedGamesOwned.Count > 0) {
				return new Dictionary<uint, string>(CachedGamesOwned);
			}
		}

		if (cachedOnly) {
			return null;
		}

		Dictionary<uint, string>? gamesOwned = await Bot.ArchiHandler.GetOwnedGames(Bot.SteamID).ConfigureAwait(false);

		if (gamesOwned?.Count > 0) {
			lock (CachedGamesOwned) {
				if (CachedGamesOwned.Count == 0) {
					foreach ((uint appID, string gameName) in gamesOwned) {
						CachedGamesOwned[appID] = gameName;
					}

					CachedGamesOwned.TrimExcess();
				}
			}
		}

		return gamesOwned;
	}

	private static HashSet<EAssetRarity>? ParseAssetRarities(string assetRaritiesText) {
		ArgumentException.ThrowIfNullOrEmpty(assetRaritiesText);

		string[] assetRaritiesArgs = assetRaritiesText.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		HashSet<EAssetRarity> assetRarities = [];

		foreach (string assetRarityArg in assetRaritiesArgs) {
			if (!Enum.TryParse(assetRarityArg, true, out EAssetRarity assetRarity) || !Enum.IsDefined(assetRarity)) {
				return null;
			}

			assetRarities.Add(assetRarity);
		}

		return assetRarities;
	}

	private async Task<string?> Response2FA(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		(bool success, string? token, string message) = await Bot.Actions.GenerateTwoFactorAuthenticationToken().ConfigureAwait(false);

		return FormatBotResponse(success && !string.IsNullOrEmpty(token) ? Strings.FormatBotAuthenticatorToken(token) : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> Response2FA(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.Response2FA(GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> Response2FAConfirm(EAccess access, bool confirm) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (!Bot.HasMobileAuthenticator) {
			return FormatBotResponse(Strings.BotNoASFAuthenticator);
		}

		(bool success, _, string message) = await Bot.Actions.HandleTwoFactorAuthenticationConfirmations(confirm).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> Response2FAConfirm(EAccess access, string botNames, bool confirm, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.Response2FAConfirm(GetProxyAccess(bot, access, steamID), confirm))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseAddLicense(EAccess access, string query) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(query);

		if (access < EAccess.Operator) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		StringBuilder response = new();

		string[] entries = query.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		foreach (string entry in entries) {
			uint gameID;
			string type;

			int index = entry.IndexOf('/', StringComparison.Ordinal);

			if ((index > 0) && (entry.Length > index + 1)) {
				if (!uint.TryParse(entry[(index + 1)..], out gameID) || (gameID == 0)) {
					response.AppendLine(FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(gameID))));

					continue;
				}

				type = entry[..index];
			} else if (uint.TryParse(entry, out gameID) && (gameID > 0)) {
				type = "SUB";
			} else {
				response.AppendLine(FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(gameID))));

				continue;
			}

			switch (type.ToUpperInvariant()) {
				case "A" or "APP": {
					HashSet<uint>? packageIDs = ASF.GlobalDatabase?.GetPackageIDs(gameID, Bot.OwnedPackages.Keys, 1);

					if (packageIDs is { Count: > 0 }) {
						response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicense($"app/{gameID}", $"{EResult.Fail}/{EPurchaseResultDetail.AlreadyPurchased}")));

						break;
					}

					(EResult result, IReadOnlyCollection<uint>? grantedApps, IReadOnlyCollection<uint>? grantedPackages) = await Bot.Actions.AddFreeLicenseApp(gameID).ConfigureAwait(false);

					if (((grantedApps == null) || (grantedApps.Count == 0)) && ((grantedPackages == null) || (grantedPackages.Count == 0))) {
						response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicense($"app/{gameID}", result)));

						break;
					}

					grantedApps ??= [];
					grantedPackages ??= [];

					response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicenseWithItems($"app/{gameID}", result, string.Join(", ", grantedApps.Select(static appID => $"app/{appID}").Union(grantedPackages.Select(static subID => $"sub/{subID}"))))));

					break;
				}

				case "S" or "SUB": {
					if (Bot.OwnedPackages.ContainsKey(gameID)) {
						response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicense($"sub/{gameID}", $"{EResult.Fail}/{EPurchaseResultDetail.AlreadyPurchased}")));

						break;
					}

					(EResult result, EPurchaseResultDetail purchaseResult) = await Bot.Actions.AddFreeLicensePackage(gameID).ConfigureAwait(false);

					response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicense($"sub/{gameID}", $"{result}/{purchaseResult}")));

					break;
				}

				default: {
					response.AppendLine(FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(gameID))));

					continue;
				}
			}
		}

		return response.Length > 0 ? response.ToString() : null;
	}

	private static async Task<string?> ResponseAddLicense(EAccess access, string botNames, string query, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(query);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAddLicense(GetProxyAccess(bot, access, steamID), query))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseAdvancedLoot(EAccess access, string targetAppID, string targetContextID) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppID);
		ArgumentException.ThrowIfNullOrEmpty(targetContextID);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(appID)));
		}

		if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(contextID)));
		}

		(bool success, string message) = await Bot.Actions.SendInventory(appID, contextID).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> ResponseAdvancedLoot(EAccess access, string botNames, string appID, string contextID, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(appID);
		ArgumentException.ThrowIfNullOrEmpty(contextID);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedLoot(GetProxyAccess(bot, access, steamID), appID, contextID))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseAdvancedLootByAssetRarity(EAccess access, string targetAppID, string targetContextID, string assetRaritiesText) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppID);
		ArgumentException.ThrowIfNullOrEmpty(targetContextID);
		ArgumentException.ThrowIfNullOrEmpty(assetRaritiesText);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(appID)));
		}

		if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(contextID)));
		}

		HashSet<EAssetRarity>? assetRarities = ParseAssetRarities(assetRaritiesText);

		if ((assetRarities == null) || (assetRarities.Count == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(assetRarities)));
		}

		(bool success, string message) = await Bot.Actions.SendInventory(appID, contextID, filterFunction: item => assetRarities.Contains(item.Rarity)).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> ResponseAdvancedLootByAssetRarity(EAccess access, string botNames, string appID, string contextID, string assetRaritiesText, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(appID);
		ArgumentException.ThrowIfNullOrEmpty(contextID);
		ArgumentException.ThrowIfNullOrEmpty(assetRaritiesText);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedLootByAssetRarity(GetProxyAccess(bot, access, steamID), appID, contextID, assetRaritiesText))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseAdvancedRedeem(EAccess access, string options, string keys, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(options);
		ArgumentException.ThrowIfNullOrEmpty(keys);

		if (access < EAccess.Operator) {
			return null;
		}

		string[] flags = options.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (flags.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(flags)));
		}

		ERedeemFlags redeemFlags = ERedeemFlags.None;

		foreach (string flag in flags) {
			switch (flag.ToUpperInvariant()) {
				case "FAWK" or "FORCEASSUMEWALLETKEY":
					redeemFlags |= ERedeemFlags.ForceAssumeWalletKeyOnBadActivationCode;

					break;
				case "FD" or "FORCEDISTRIBUTING":
					redeemFlags |= ERedeemFlags.ForceDistributing;

					break;
				case "FF" or "FORCEFORWARDING":
					redeemFlags |= ERedeemFlags.ForceForwarding;

					break;
				case "FKMG" or "FORCEKEEPMISSINGGAMES":
					redeemFlags |= ERedeemFlags.ForceKeepMissingGames;

					break;
				case "SAWK" or "SKIPASSUMEWALLETKEY":
					redeemFlags |= ERedeemFlags.SkipAssumeWalletKeyOnBadActivationCode;

					break;
				case "SD" or "SKIPDISTRIBUTING":
					redeemFlags |= ERedeemFlags.SkipDistributing;

					break;
				case "SF" or "SKIPFORWARDING":
					redeemFlags |= ERedeemFlags.SkipForwarding;

					break;
				case "SI" or "SKIPINITIAL":
					redeemFlags |= ERedeemFlags.SkipInitial;

					break;
				case "SKMG" or "SKIPKEEPMISSINGGAMES":
					redeemFlags |= ERedeemFlags.SkipKeepMissingGames;

					break;
				case "V" or "VALIDATE":
					redeemFlags |= ERedeemFlags.Validate;

					break;
				default:
					return FormatBotResponse(Strings.FormatErrorIsInvalid(flag));
			}
		}

		return await ResponseRedeem(access, keys, steamID, redeemFlags).ConfigureAwait(false);
	}

	private static async Task<string?> ResponseAdvancedRedeem(EAccess access, string botNames, string options, string keys, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(options);
		ArgumentException.ThrowIfNullOrEmpty(keys);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedRedeem(GetProxyAccess(bot, access, steamID), options, keys, steamID))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseAdvancedTransfer(EAccess access, uint appID, ulong contextID, Bot targetBot) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(contextID);
		ArgumentNullException.ThrowIfNull(targetBot);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (!targetBot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.TargetBotNotConnected);
		}

		(bool success, string message) = await Bot.Actions.SendInventory(appID, contextID, targetBot.SteamID).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private async Task<string?> ResponseAdvancedTransfer(EAccess access, string targetAppID, string targetContextID, string botNameTo) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppID);
		ArgumentException.ThrowIfNullOrEmpty(targetContextID);
		ArgumentException.ThrowIfNullOrEmpty(botNameTo);

		Bot? targetBot = Bot.GetBot(botNameTo);

		if (targetBot == null) {
			return access >= EAccess.Owner ? FormatBotResponse(Strings.FormatBotNotFound(botNameTo)) : null;
		}

		if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(appID)));
		}

		if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(contextID)));
		}

		return await ResponseAdvancedTransfer(access, appID, contextID, targetBot).ConfigureAwait(false);
	}

	private static async Task<string?> ResponseAdvancedTransfer(EAccess access, string botNames, string targetAppID, string targetContextID, string botNameTo, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetAppID);
		ArgumentException.ThrowIfNullOrEmpty(targetContextID);
		ArgumentException.ThrowIfNullOrEmpty(botNameTo);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
			return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(appID)));
		}

		if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
			return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(contextID)));
		}

		Bot? targetBot = Bot.GetBot(botNameTo);

		if (targetBot == null) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNameTo)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedTransfer(GetProxyAccess(bot, access, steamID), appID, contextID, targetBot))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseAdvancedTransferByAssetRarity(EAccess access, uint appID, ulong contextID, Bot targetBot, HashSet<EAssetRarity> assetRarities) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentOutOfRangeException.ThrowIfZero(appID);
		ArgumentOutOfRangeException.ThrowIfZero(contextID);
		ArgumentNullException.ThrowIfNull(targetBot);

		if ((assetRarities == null) || (assetRarities.Count == 0)) {
			throw new ArgumentNullException(nameof(assetRarities));
		}

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (!targetBot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.TargetBotNotConnected);
		}

		(bool success, string message) = await Bot.Actions.SendInventory(appID, contextID, targetBot.SteamID, filterFunction: item => assetRarities.Contains(item.Rarity)).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private async Task<string?> ResponseAdvancedTransferByAssetRarity(EAccess access, string targetAppID, string targetContextID, string botNameTo, string assetRaritiesText) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppID);
		ArgumentException.ThrowIfNullOrEmpty(targetContextID);
		ArgumentException.ThrowIfNullOrEmpty(botNameTo);
		ArgumentException.ThrowIfNullOrEmpty(assetRaritiesText);

		Bot? targetBot = Bot.GetBot(botNameTo);

		if (targetBot == null) {
			return access >= EAccess.Owner ? FormatBotResponse(Strings.FormatBotNotFound(botNameTo)) : null;
		}

		if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(appID)));
		}

		if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(contextID)));
		}

		HashSet<EAssetRarity>? assetRarities = ParseAssetRarities(assetRaritiesText);

		if ((assetRarities == null) || (assetRarities.Count == 0)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(assetRarities)));
		}

		return await ResponseAdvancedTransferByAssetRarity(access, appID, contextID, targetBot, assetRarities).ConfigureAwait(false);
	}

	private static async Task<string?> ResponseAdvancedTransferByAssetRarity(EAccess access, string botNames, string targetAppID, string targetContextID, string botNameTo, string assetRaritiesText, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetAppID);
		ArgumentException.ThrowIfNullOrEmpty(targetContextID);
		ArgumentException.ThrowIfNullOrEmpty(botNameTo);
		ArgumentException.ThrowIfNullOrEmpty(assetRaritiesText);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		if (!uint.TryParse(targetAppID, out uint appID) || (appID == 0)) {
			return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(appID)));
		}

		if (!ulong.TryParse(targetContextID, out ulong contextID) || (contextID == 0)) {
			return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(contextID)));
		}

		Bot? targetBot = Bot.GetBot(botNameTo);

		if (targetBot == null) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNameTo)) : null;
		}

		HashSet<EAssetRarity>? assetRarities = ParseAssetRarities(assetRaritiesText);

		if ((assetRarities == null) || (assetRarities.Count == 0)) {
			return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(assetRarities)));
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseAdvancedTransferByAssetRarity(GetProxyAccess(bot, access, steamID), appID, contextID, targetBot, assetRarities))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseBackgroundGamesRedeemer(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		uint count = Bot.GamesToRedeemInBackgroundCount;

		return FormatBotResponse(Strings.FormatBotGamesToRedeemInBackgroundCount(count));
	}

	private static async Task<string?> ResponseBackgroundGamesRedeemer(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseBackgroundGamesRedeemer(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseBackgroundGamesRedeemerClear(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		Bot.BotDatabase.ClearGamesToRedeemInBackground();

		return FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseBackgroundGamesRedeemerClear(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseBackgroundGamesRedeemerClear(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private static string? ResponseEncrypt(EAccess access, string cryptoMethodText, string stringToEncrypt) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(cryptoMethodText);
		ArgumentException.ThrowIfNullOrEmpty(stringToEncrypt);

		if (access < EAccess.Owner) {
			return null;
		}

		if (!Enum.TryParse(cryptoMethodText, true, out ArchiCryptoHelper.ECryptoMethod cryptoMethod)) {
			return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(cryptoMethod)));
		}

		string? encryptedString = Actions.Encrypt(cryptoMethod, stringToEncrypt);

		return FormatStaticResponse(!string.IsNullOrEmpty(encryptedString) ? Strings.FormatResult(encryptedString) : Strings.WarningFailed);
	}

	private static string? ResponseExit(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Owner) {
			return null;
		}

		(bool success, string message) = Actions.Exit();

		return FormatStaticResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private async Task<string?> ResponseFarm(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (Bot.CardsFarmer.NowFarming) {
			await Bot.CardsFarmer.StopFarming().ConfigureAwait(false);
		}

		Utilities.InBackground(Bot.CardsFarmer.StartFarming);

		return FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseFarm(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseFarm(GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseFarmingBlacklist(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		return access < EAccess.Master ? null : FormatBotResponse(Bot.BotDatabase.FarmingBlacklistAppIDs.Count == 0 ? Strings.FormatErrorIsEmpty(nameof(Bot.BotDatabase.FarmingBlacklistAppIDs)) : string.Join(", ", Bot.BotDatabase.FarmingBlacklistAppIDs));
	}

	private static async Task<string?> ResponseFarmingBlacklist(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseFarmingBlacklist(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseFarmingBlacklistAdd(EAccess access, string targetAppIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		if (access < EAccess.Master) {
			return null;
		}

		string[] targets = targetAppIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (targets.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(targets)));
		}

		HashSet<uint> appIDs = [];

		foreach (string target in targets) {
			if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
				return FormatBotResponse(Strings.FormatErrorParsingObject(nameof(appID)));
			}

			appIDs.Add(appID);
		}

		if (!Bot.BotDatabase.FarmingBlacklistAppIDs.AddRange(appIDs)) {
			return FormatBotResponse(Strings.NothingFound);
		}

		if (Bot.CardsFarmer.NowFarming && Bot.CardsFarmer.GamesToFarmReadOnly.Any(game => appIDs.Contains(game.AppID))) {
			Utilities.InBackground(async () => {
					await Bot.CardsFarmer.StopFarming().ConfigureAwait(false);
					await Bot.CardsFarmer.StartFarming().ConfigureAwait(false);
				}
			);
		}

		return FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseFarmingBlacklistAdd(EAccess access, string botNames, string targetAppIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseFarmingBlacklistAdd(GetProxyAccess(bot, access, steamID), targetAppIDs)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseFarmingBlacklistRemove(EAccess access, string targetAppIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		if (access < EAccess.Master) {
			return null;
		}

		string[] targets = targetAppIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (targets.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(targets)));
		}

		HashSet<uint> appIDs = [];

		foreach (string target in targets) {
			if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
				return FormatBotResponse(Strings.FormatErrorParsingObject(nameof(appID)));
			}

			appIDs.Add(appID);
		}

		if (!Bot.BotDatabase.FarmingBlacklistAppIDs.RemoveRange(appIDs)) {
			return FormatBotResponse(Strings.NothingFound);
		}

		if (!Bot.CardsFarmer.NowFarming) {
			Utilities.InBackground(Bot.CardsFarmer.StartFarming);
		}

		return FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseFarmingBlacklistRemove(EAccess access, string botNames, string targetAppIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseFarmingBlacklistRemove(GetProxyAccess(bot, access, steamID), targetAppIDs)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseFarmingQueue(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		return access < EAccess.Master ? null : FormatBotResponse(Bot.BotDatabase.FarmingPriorityQueueAppIDs.Count == 0 ? Strings.FormatErrorIsEmpty(nameof(Bot.BotDatabase.FarmingPriorityQueueAppIDs)) : string.Join(", ", Bot.BotDatabase.FarmingPriorityQueueAppIDs));
	}

	private static async Task<string?> ResponseFarmingQueue(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseFarmingQueue(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseFarmingQueueAdd(EAccess access, string targetAppIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		if (access < EAccess.Master) {
			return null;
		}

		string[] targets = targetAppIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (targets.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(targets)));
		}

		HashSet<uint> appIDs = [];

		foreach (string target in targets) {
			if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
				return FormatBotResponse(Strings.FormatErrorParsingObject(nameof(appID)));
			}

			appIDs.Add(appID);
		}

		if (!Bot.BotDatabase.FarmingPriorityQueueAppIDs.AddRange(appIDs)) {
			return FormatBotResponse(Strings.NothingFound);
		}

		switch (Bot.CardsFarmer.NowFarming) {
			case false when Bot.BotConfig.FarmingPreferences.HasFlag(BotConfig.EFarmingPreferences.FarmPriorityQueueOnly):
				Utilities.InBackground(Bot.CardsFarmer.StartFarming);

				break;
			case true when Bot.CardsFarmer.GamesToFarmReadOnly.Any(game => appIDs.Contains(game.AppID)):
				Utilities.InBackground(async () => {
						await Bot.CardsFarmer.StopFarming().ConfigureAwait(false);
						await Bot.CardsFarmer.StartFarming().ConfigureAwait(false);
					}
				);

				break;
		}

		return FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseFarmingQueueAdd(EAccess access, string botNames, string targetAppIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseFarmingQueueAdd(GetProxyAccess(bot, access, steamID), targetAppIDs)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseFarmingQueueRemove(EAccess access, string targetAppIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		if (access < EAccess.Master) {
			return null;
		}

		string[] targets = targetAppIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (targets.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(targets)));
		}

		HashSet<uint> appIDs = [];

		foreach (string target in targets) {
			if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
				return FormatBotResponse(Strings.FormatErrorParsingObject(nameof(appID)));
			}

			appIDs.Add(appID);
		}

		if (!Bot.BotDatabase.FarmingPriorityQueueAppIDs.RemoveRange(appIDs)) {
			return FormatBotResponse(Strings.NothingFound);
		}

		if (Bot.CardsFarmer.NowFarming && Bot.CardsFarmer.GamesToFarmReadOnly.Any(game => appIDs.Contains(game.AppID))) {
			Utilities.InBackground(async () => {
					await Bot.CardsFarmer.StopFarming().ConfigureAwait(false);
					await Bot.CardsFarmer.StartFarming().ConfigureAwait(false);
				}
			);
		}

		return FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseFarmingQueueRemove(EAccess access, string botNames, string targetAppIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseFarmingQueueRemove(GetProxyAccess(bot, access, steamID), targetAppIDs)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private static string? ResponseHash(EAccess access, string hashingMethodText, string stringToHash) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(hashingMethodText);
		ArgumentException.ThrowIfNullOrEmpty(stringToHash);

		if (access < EAccess.Owner) {
			return null;
		}

		if (!Enum.TryParse(hashingMethodText, true, out ArchiCryptoHelper.EHashingMethod hashingMethod)) {
			return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(hashingMethod)));
		}

		string hash = Actions.Hash(hashingMethod, stringToHash);

		return FormatStaticResponse(!string.IsNullOrEmpty(hash) ? Strings.FormatResult(hash) : Strings.WarningFailed);
	}

	private string? ResponseHelp(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		return access >= EAccess.FamilySharing ? FormatBotResponse($"{SharedInfo.ProjectURL}/wiki/Commands") : null;
	}

	private string? ResponseInput(EAccess access, string propertyName, string inputValue) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(propertyName);
		ArgumentException.ThrowIfNullOrEmpty(inputValue);

		if (access < EAccess.Master) {
			return null;
		}

		bool headless = Program.Service || (ASF.GlobalConfig?.Headless ?? GlobalConfig.DefaultHeadless);

		if (!headless) {
			return FormatBotResponse(Strings.ErrorFunctionOnlyInHeadlessMode);
		}

		if (!Enum.TryParse(propertyName, true, out ASF.EUserInputType inputType) || (inputType == ASF.EUserInputType.None) || !Enum.IsDefined(inputType)) {
			return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(inputType)));
		}

		bool result = Bot.SetUserInput(inputType, inputValue);

		return FormatBotResponse(result ? Strings.Done : Strings.WarningFailed);
	}

	private static async Task<string?> ResponseInput(EAccess access, string botNames, string propertyName, string inputValue, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(propertyName);
		ArgumentException.ThrowIfNullOrEmpty(inputValue);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseInput(GetProxyAccess(bot, access, steamID), propertyName, inputValue)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseInventory(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Operator) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		ImmutableDictionary<uint, InventoryAppData>? inventory = await Bot.ArchiWebHandler.GetInventoryContextData().ConfigureAwait(false);

		if (inventory == null) {
			return FormatBotResponse(Strings.WarningFailed);
		}

		if (inventory.Count == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(inventory)));
		}

		StringBuilder response = new();

		foreach (InventoryAppData appData in inventory.Values) {
			foreach (InventoryContextData contextData in appData.Contexts.Values) {
				if (response.Length > 0) {
					response.AppendLine();
				}

				response.Append(FormatBotResponse(Strings.FormatBotInventory(appData.AppID, contextData.ID, appData.Name, contextData.Name, contextData.AssetsCount)));
			}
		}

		return response.ToString();
	}

	private static async Task<string?> ResponseInventory(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseInventory(GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseLevel(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		uint? level = await Bot.ArchiHandler.GetLevel().ConfigureAwait(false);

		return FormatBotResponse(level.HasValue ? Strings.FormatBotLevel(level.Value) : Strings.WarningFailed);
	}

	private static async Task<string?> ResponseLevel(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseLevel(GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseLoot(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (Bot.BotConfig.LootableTypes.Count == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(Bot.BotConfig.LootableTypes)));
		}

		(bool success, string message) = await Bot.Actions.SendInventory(filterFunction: item => Bot.BotConfig.LootableTypes.Contains(item.Type)).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> ResponseLoot(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseLoot(GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseLootByRealAppIDs(EAccess access, string realAppIDsText, bool exclude) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(realAppIDsText);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (Bot.BotConfig.LootableTypes.Count == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(Bot.BotConfig.LootableTypes)));
		}

		string[] appIDTexts = realAppIDsText.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (appIDTexts.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(appIDTexts)));
		}

		HashSet<uint> realAppIDs = [];

		foreach (string appIDText in appIDTexts) {
			if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
				return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(appID)));
			}

			realAppIDs.Add(appID);
		}

		(bool success, string message) = await Bot.Actions.SendInventory(filterFunction: item => Bot.BotConfig.LootableTypes.Contains(item.Type) && (exclude ^ realAppIDs.Contains(item.RealAppID))).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> ResponseLootByRealAppIDs(EAccess access, string botNames, string realAppIDsText, bool exclude, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(realAppIDsText);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseLootByRealAppIDs(GetProxyAccess(bot, access, steamID), realAppIDsText, exclude))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseMatchActivelyBlacklist(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		return access < EAccess.Master ? null : FormatBotResponse(Bot.BotDatabase.MatchActivelyBlacklistAppIDs.Count == 0 ? Strings.FormatErrorIsEmpty(nameof(Bot.BotDatabase.MatchActivelyBlacklistAppIDs)) : string.Join(", ", Bot.BotDatabase.MatchActivelyBlacklistAppIDs));
	}

	private static async Task<string?> ResponseMatchActivelyBlacklist(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseMatchActivelyBlacklist(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseMatchActivelyBlacklistAdd(EAccess access, string targetAppIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		if (access < EAccess.Master) {
			return null;
		}

		string[] targets = targetAppIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (targets.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(targets)));
		}

		HashSet<uint> appIDs = [];

		foreach (string target in targets) {
			if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
				return FormatBotResponse(Strings.FormatErrorParsingObject(nameof(appID)));
			}

			appIDs.Add(appID);
		}

		return FormatBotResponse(Bot.BotDatabase.MatchActivelyBlacklistAppIDs.AddRange(appIDs) ? Strings.Done : Strings.NothingFound);
	}

	private static async Task<string?> ResponseMatchActivelyBlacklistAdd(EAccess access, string botNames, string targetAppIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseMatchActivelyBlacklistAdd(GetProxyAccess(bot, access, steamID), targetAppIDs)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseMatchActivelyBlacklistRemove(EAccess access, string targetAppIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		if (access < EAccess.Master) {
			return null;
		}

		string[] targets = targetAppIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (targets.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(targets)));
		}

		HashSet<uint> appIDs = [];

		foreach (string target in targets) {
			if (!uint.TryParse(target, out uint appID) || (appID == 0)) {
				return FormatBotResponse(Strings.FormatErrorParsingObject(nameof(appID)));
			}

			appIDs.Add(appID);
		}

		return FormatBotResponse(Bot.BotDatabase.MatchActivelyBlacklistAppIDs.RemoveRange(appIDs) ? Strings.Done : Strings.NothingFound);
	}

	private static async Task<string?> ResponseMatchActivelyBlacklistRemove(EAccess access, string botNames, string targetAppIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetAppIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseMatchActivelyBlacklistRemove(GetProxyAccess(bot, access, steamID), targetAppIDs)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseNickname(EAccess access, string nickname) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(nickname);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		Bot.SteamFriends.SetPersonaName(nickname);

		// Nickname change affects the current persona state, reset it back to wanted one
		Bot.ResetPersonaState();

		return FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseNickname(EAccess access, string botNames, string nickname, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(nickname);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseNickname(GetProxyAccess(bot, access, steamID), nickname)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<(string? Response, Dictionary<string, string>? OwnedGames)> ResponseOwns(EAccess access, string query) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(query);

		if (access < EAccess.Operator) {
			return (null, null);
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return (FormatBotResponse(Strings.BotNotConnected), null);
		}

		Dictionary<uint, string>? gamesOwned = await FetchGamesOwned(true).ConfigureAwait(false);

		StringBuilder response = new();
		Dictionary<string, string> result = new(StringComparer.Ordinal);

		string[] entries = query.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		foreach (string entry in entries) {
			string game;
			string type;

			int index = entry.IndexOf('/', StringComparison.Ordinal);

			if ((index > 0) && (entry.Length > index + 1)) {
				game = entry[(index + 1)..];
				type = entry[..index];
			} else if (uint.TryParse(entry, out uint appID) && (appID > 0)) {
				game = entry;
				type = "APP";
			} else {
				game = entry;
				type = "NAME";
			}

			switch (type.ToUpperInvariant()) {
				case "A" or "APP" when uint.TryParse(game, out uint appID) && (appID > 0):
					HashSet<uint>? packageIDs = ASF.GlobalDatabase?.GetPackageIDs(appID, Bot.OwnedPackages.Keys, 1);

					if (packageIDs?.Count > 0) {
						if ((gamesOwned != null) && gamesOwned.TryGetValue(appID, out string? cachedGameName)) {
							result[$"app/{appID}"] = cachedGameName;
							response.AppendLine(FormatBotResponse(Strings.FormatBotOwnedAlreadyWithName($"app/{appID}", cachedGameName)));
						} else {
							result[$"app/{appID}"] = appID.ToString(CultureInfo.InvariantCulture);
							response.AppendLine(FormatBotResponse(Strings.FormatBotOwnedAlready($"app/{appID}")));
						}
					} else {
						if (gamesOwned == null) {
							gamesOwned = await FetchGamesOwned().ConfigureAwait(false);

							if (gamesOwned == null) {
								response.AppendLine(FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(gamesOwned))));

								break;
							}
						}

						if (gamesOwned.TryGetValue(appID, out string? gameName)) {
							result[$"app/{appID}"] = gameName;
							response.AppendLine(FormatBotResponse(Strings.FormatBotOwnedAlreadyWithName($"app/{appID}", gameName)));
						} else {
							response.AppendLine(FormatBotResponse(Strings.FormatBotNotOwnedYet($"app/{appID}")));
						}
					}

					break;
				case "R" or "REGEX":
					Regex regex;

					try {
#pragma warning disable CA3012 // We're aware of a potential denial of service here, this is why we limit maximum matching time to a sane value
						regex = new Regex(game, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
#pragma warning restore CA3012 // We're aware of a potential denial of service here, this is why we limit maximum matching time to a sane value
					} catch (ArgumentException e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
						response.AppendLine(FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(regex))));

						break;
					}

					if (gamesOwned == null) {
						gamesOwned = await FetchGamesOwned().ConfigureAwait(false);

						if (gamesOwned == null) {
							response.AppendLine(FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(gamesOwned))));

							break;
						}
					}

					bool foundWithRegex = false;

					try {
						foreach ((uint appID, string gameName) in gamesOwned.Where(gameOwned => regex.IsMatch(gameOwned.Value))) {
							foundWithRegex = true;

							result[$"app/{appID}"] = gameName;
							response.AppendLine(FormatBotResponse(Strings.FormatBotOwnedAlreadyWithName($"app/{appID}", gameName)));
						}
					} catch (RegexMatchTimeoutException e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
						response.AppendLine(FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(regex))));

						break;
					}

					if (!foundWithRegex) {
						response.AppendLine(FormatBotResponse(Strings.FormatBotNotOwnedYet(entry)));
					}

					continue;
				case "S" or "SUB" when uint.TryParse(game, out uint packageID) && (packageID > 0):
					if (Bot.OwnedPackages.ContainsKey(packageID)) {
						result[$"sub/{packageID}"] = packageID.ToString(CultureInfo.InvariantCulture);
						response.AppendLine(FormatBotResponse(Strings.FormatBotOwnedAlready($"sub/{packageID}")));
					} else {
						response.AppendLine(FormatBotResponse(Strings.FormatBotNotOwnedYet($"sub/{packageID}")));
					}

					break;
				default:
					if (gamesOwned == null) {
						gamesOwned = await FetchGamesOwned().ConfigureAwait(false);

						if (gamesOwned == null) {
							response.AppendLine(FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(gamesOwned))));

							break;
						}
					}

					bool foundWithName = false;

					foreach ((uint appID, string gameName) in gamesOwned.Where(gameOwned => gameOwned.Value.Contains(game, StringComparison.OrdinalIgnoreCase))) {
						foundWithName = true;

						result[$"app/{appID}"] = gameName;
						response.AppendLine(FormatBotResponse(Strings.FormatBotOwnedAlreadyWithName($"app/{appID}", gameName)));
					}

					if (!foundWithName) {
						response.AppendLine(FormatBotResponse(Strings.FormatBotNotOwnedYet(entry)));
					}

					break;
			}
		}

		return (response.Length > 0 ? response.ToString() : FormatBotResponse(Strings.FormatBotNotOwnedYet(query)), result);
	}

	private static async Task<string?> ResponseOwns(EAccess access, string botNames, string query, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(query);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<(string? Response, Dictionary<string, string>? OwnedGames)> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseOwns(GetProxyAccess(bot, access, steamID), query))).ConfigureAwait(false);

		List<(string Response, Dictionary<string, string> OwnedGames)> validResults = [..results.Where(static result => !string.IsNullOrEmpty(result.Response) && (result.OwnedGames != null))!];

		if (validResults.Count == 0) {
			return null;
		}

		Dictionary<string, (ushort Count, string? GameName)> ownedGamesStats = new(StringComparer.Ordinal);

		foreach ((string gameID, string gameName) in validResults.Where(static validResult => validResult.OwnedGames.Count > 0).SelectMany(static validResult => validResult.OwnedGames)) {
			(ushort Count, string? GameName) ownedGameStats = ownedGamesStats.GetValueOrDefault(gameID);

			ownedGameStats.Count++;

			if (!string.IsNullOrEmpty(gameName)) {
				ownedGameStats.GameName = gameName;
			}

			ownedGamesStats[gameID] = ownedGameStats;
		}

		IEnumerable<string> extraResponses = ownedGamesStats.Select(kv => FormatStaticResponse(Strings.FormatBotOwnsOverviewPerGame(kv.Value.Count, validResults.Count, $"{kv.Key}{(!string.IsNullOrEmpty(kv.Value.GameName) ? $" | {kv.Value.GameName}" : "")}")));

		return string.Join(Environment.NewLine, validResults.Select(static result => result.Response).Concat(extraResponses));
	}

	private async Task<string?> ResponsePause(EAccess access, bool permanent, string? resumeInSecondsText = null) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.FamilySharing) {
			return null;
		}

		if (permanent && (access < EAccess.Operator)) {
			return FormatBotResponse(Strings.ErrorAccessDenied);
		}

		ushort resumeInSeconds = 0;

		if (!string.IsNullOrEmpty(resumeInSecondsText) && (!ushort.TryParse(resumeInSecondsText, out resumeInSeconds) || (resumeInSeconds == 0))) {
			return Strings.FormatErrorIsInvalid(nameof(resumeInSecondsText));
		}

		(bool success, string message) = await Bot.Actions.Pause(permanent, resumeInSeconds).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> ResponsePause(EAccess access, string botNames, bool permanent, string? resumeInSecondsText = null, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePause(GetProxyAccess(bot, access, steamID), permanent, resumeInSecondsText))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponsePlay(EAccess access, HashSet<uint> gameIDs, string? gameName = null) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentNullException.ThrowIfNull(gameIDs);

		if (gameIDs.Count > ArchiHandler.MaxGamesPlayedConcurrently) {
			throw new ArgumentOutOfRangeException(nameof(gameIDs));
		}

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		(bool success, string message) = await Bot.Actions.Play(gameIDs, gameName).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private async Task<string?> ResponsePlay(EAccess access, string targetGameIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetGameIDs);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		string[] games = targetGameIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (games.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(games)));
		}

		HashSet<uint> gamesToPlay = new(Math.Min(games.Length, ArchiHandler.MaxGamesPlayedConcurrently));
		StringBuilder gameName = new();

		foreach (string game in games) {
			if (!uint.TryParse(game, out uint gameID) || (gameID == 0)) {
				if (gameName.Length > 0) {
					gameName.Append(' ');
				}

				gameName.Append(game);

				continue;
			}

			if (gamesToPlay.Count >= ArchiHandler.MaxGamesPlayedConcurrently) {
				return FormatBotResponse(Strings.FormatWarningFailedWithError($"{nameof(gamesToPlay)} > {ArchiHandler.MaxGamesPlayedConcurrently}"));
			}

			gamesToPlay.Add(gameID);
		}

		return await ResponsePlay(access, gamesToPlay, gameName.Length > 0 ? gameName.ToString() : null).ConfigureAwait(false);
	}

	private static async Task<string?> ResponsePlay(EAccess access, string botNames, string targetGameIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetGameIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePlay(GetProxyAccess(bot, access, steamID), targetGameIDs))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponsePointsBalance(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		long? points = await Bot.ArchiHandler.GetPointsBalance().ConfigureAwait(false);

		return FormatBotResponse(points.HasValue ? Strings.FormatBotPointsBalance(points) : Strings.WarningFailed);
	}

	private static async Task<string?> ResponsePointsBalance(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePointsBalance(GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponsePrivacy(EAccess access, string privacySettingsText) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(privacySettingsText);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		// There are only 7 privacy settings
		const byte privacySettings = 7;

		string[] privacySettingsArgs = privacySettingsText.Split(SharedInfo.ListElementSeparators, privacySettings + 1, StringSplitOptions.RemoveEmptyEntries);

		switch (privacySettingsArgs.Length) {
			case 0:
				return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(privacySettingsArgs)));
			case > privacySettings:
				return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(privacySettingsArgs)));
		}

		ArchiHandler.EPrivacySetting profile = ArchiHandler.EPrivacySetting.Private;
		ArchiHandler.EPrivacySetting ownedGames = ArchiHandler.EPrivacySetting.Private;
		ArchiHandler.EPrivacySetting playtime = ArchiHandler.EPrivacySetting.Private;
		ArchiHandler.EPrivacySetting friendsList = ArchiHandler.EPrivacySetting.Private;
		ArchiHandler.EPrivacySetting inventory = ArchiHandler.EPrivacySetting.Private;
		ArchiHandler.EPrivacySetting inventoryGifts = ArchiHandler.EPrivacySetting.Private;
		UserPrivacy.ECommentPermission comments = UserPrivacy.ECommentPermission.Private;

		// Converting digits to enum
		for (byte index = 0; index < privacySettingsArgs.Length; index++) {
			if (!Enum.TryParse(privacySettingsArgs[index], true, out ArchiHandler.EPrivacySetting privacySetting) || (privacySetting == ArchiHandler.EPrivacySetting.Unknown) || !Enum.IsDefined(privacySetting)) {
				return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(privacySettingsArgs)));
			}

			// Child setting can't be less restrictive than its parent
			switch (index) {
				case 0:
					// Profile
					profile = privacySetting;

					break;
				case 1:
					// OwnedGames, child of Profile
					if (profile < privacySetting) {
						return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(ownedGames)));
					}

					ownedGames = privacySetting;

					break;
				case 2:
					// Playtime, child of OwnedGames
					if (ownedGames < privacySetting) {
						return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(playtime)));
					}

					playtime = privacySetting;

					break;
				case 3:
					// FriendsList, child of Profile
					if (profile < privacySetting) {
						return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(ownedGames)));
					}

					friendsList = privacySetting;

					break;
				case 4:
					// Inventory, child of Profile
					if (profile < privacySetting) {
						return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(inventory)));
					}

					inventory = privacySetting;

					break;
				case 5:
					// InventoryGifts, child of Inventory
					if (inventory < privacySetting) {
						return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(inventoryGifts)));
					}

					inventoryGifts = privacySetting;

					break;
				case 6:
					// Comments, child of Profile
					if (profile < privacySetting) {
						return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(comments)));
					}

					// Comments use different numbers than everything else, but we want to have this command consistent for end-user, so we'll map them
					switch (privacySetting) {
						case ArchiHandler.EPrivacySetting.FriendsOnly:
							comments = UserPrivacy.ECommentPermission.FriendsOnly;

							break;
						case ArchiHandler.EPrivacySetting.Private:
							comments = UserPrivacy.ECommentPermission.Private;

							break;
						case ArchiHandler.EPrivacySetting.Public:
							comments = UserPrivacy.ECommentPermission.Public;

							break;
						default:
							Bot.ArchiLogger.LogGenericError(Strings.FormatWarningUnknownValuePleaseReport(nameof(privacySetting), privacySetting));

							return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(privacySetting)));
					}

					break;
				default:
					Bot.ArchiLogger.LogGenericError(Strings.FormatWarningUnknownValuePleaseReport(nameof(index), index));

					return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(index)));
			}
		}

		UserPrivacy userPrivacy = new(new UserPrivacy.PrivacySettings(profile, ownedGames, playtime, friendsList, inventory, inventoryGifts), comments);

		return FormatBotResponse(await Bot.ArchiWebHandler.ChangePrivacySettings(userPrivacy).ConfigureAwait(false) ? Strings.Success : Strings.WarningFailed);
	}

	private static async Task<string?> ResponsePrivacy(EAccess access, string botNames, string privacySettingsText, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(privacySettingsText);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponsePrivacy(GetProxyAccess(bot, access, steamID), privacySettingsText))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseRedeem(EAccess access, string keysText, ulong steamID = 0, ERedeemFlags redeemFlags = ERedeemFlags.None) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(keysText);

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (Bot.Bots == null) {
			throw new InvalidOperationException(nameof(Bot.Bots));
		}

		if (access < EAccess.Operator) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		string[] keys = keysText.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (keys.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(keys)));
		}

		bool forward = !redeemFlags.HasFlag(ERedeemFlags.SkipForwarding) && (redeemFlags.HasFlag(ERedeemFlags.ForceForwarding) || Bot.BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.Forwarding));
		bool distribute = !redeemFlags.HasFlag(ERedeemFlags.SkipDistributing) && (redeemFlags.HasFlag(ERedeemFlags.ForceDistributing) || Bot.BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.Distributing));
		bool keepMissingGames = !redeemFlags.HasFlag(ERedeemFlags.SkipKeepMissingGames) && (redeemFlags.HasFlag(ERedeemFlags.ForceKeepMissingGames) || Bot.BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.KeepMissingGames));
		bool assumeWalletKeyOnBadActivationCode = !redeemFlags.HasFlag(ERedeemFlags.SkipAssumeWalletKeyOnBadActivationCode) && (redeemFlags.HasFlag(ERedeemFlags.ForceAssumeWalletKeyOnBadActivationCode) || Bot.BotConfig.RedeemingPreferences.HasFlag(BotConfig.ERedeemingPreferences.AssumeWalletKeyOnBadActivationCode));

		HashSet<string> pendingKeys = keys.ToHashSet(StringComparer.Ordinal);
		HashSet<string> unusedKeys = pendingKeys.ToHashSet(StringComparer.Ordinal);

		HashSet<Bot> rateLimitedBots = [];
		HashSet<Bot> triedBots = [];

		StringBuilder response = new();

		using (HashSet<string>.Enumerator keysEnumerator = pendingKeys.GetEnumerator()) {
			// Initial key
			string? key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;
			string? previousKey = key;

			while (!string.IsNullOrEmpty(key)) {
				string startingKey = key;

				using (IEnumerator<Bot> botsEnumerator = Bot.Bots.Where(bot => (bot.Value != Bot) && bot.Value.IsConnectedAndLoggedOn && ((access >= EAccess.Owner) || ((steamID != 0) && (bot.Value.GetAccess(steamID) >= EAccess.Operator)))).OrderByDescending(bot => Bot.BotsComparer?.Compare(bot.Key, Bot.BotName) > 0).ThenBy(static bot => bot.Key, Bot.BotsComparer).Select(static bot => bot.Value).GetEnumerator()) {
					Bot? currentBot = Bot;

					while (!string.IsNullOrEmpty(key) && (currentBot != null)) {
						if (previousKey != key) {
							triedBots.Clear();
							previousKey = key;
						}

						if (redeemFlags.HasFlag(ERedeemFlags.Validate) && !Utilities.IsValidCdKey(key)) {
							// Next key
							key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

							// Keep current bot
							continue;
						}

						if ((currentBot == Bot) && redeemFlags.HasFlag(ERedeemFlags.SkipInitial)) {
							// Either bot will be changed, or loop aborted
							currentBot = null;
						} else {
							bool skipRequest = triedBots.Contains(currentBot) || rateLimitedBots.Contains(currentBot);

							EResult result = EResult.Fail;
							EPurchaseResultDetail purchaseResultDetail = EPurchaseResultDetail.CancelledByUser;
							Dictionary<uint, string>? items = null;

							if (!skipRequest) {
								CStore_RegisterCDKey_Response? redeemResult = await currentBot.Actions.RedeemKey(key).ConfigureAwait(false);

								if (redeemResult == null) {
									result = EResult.Timeout;
									purchaseResultDetail = EPurchaseResultDetail.Timeout;
								} else if (redeemResult.purchase_receipt_info == null) {
									result = EResult.BadResponse;
									purchaseResultDetail = EPurchaseResultDetail.NoDetail;
								} else {
									result = (EResult) redeemResult.purchase_receipt_info.purchase_status;
									purchaseResultDetail = (EPurchaseResultDetail) redeemResult.purchase_result_details;

									if (redeemResult.purchase_receipt_info.line_items.Count > 0) {
										items = redeemResult.purchase_receipt_info.line_items.ToDictionary(static lineItem => lineItem.packageid, static lineItem => lineItem.line_item_description);
									}
								}
							}

							if ((result == EResult.Timeout) || (purchaseResultDetail == EPurchaseResultDetail.Timeout)) {
								response.AppendLine(FormatBotResponse(Strings.FormatBotRedeem(key, $"{result}/{purchaseResultDetail}"), currentBot.BotName));

								// Either bot will be changed, or loop aborted
								currentBot = null;
							} else {
								triedBots.Add(currentBot);

								string? balanceText = null;

								if ((purchaseResultDetail == EPurchaseResultDetail.CannotRedeemCodeFromClient) || ((purchaseResultDetail == EPurchaseResultDetail.BadActivationCode) && assumeWalletKeyOnBadActivationCode)) {
									// If it's a wallet code, we try to redeem it first, then handle the inner result as our primary one
									(EResult Result, EPurchaseResultDetail? PurchaseResult, string? BalanceText)? walletResult = await currentBot.ArchiWebHandler.RedeemWalletKey(key).ConfigureAwait(false);

									if (walletResult != null) {
										result = walletResult.Value.Result;
										purchaseResultDetail = walletResult.Value.PurchaseResult.GetValueOrDefault(walletResult.Value.Result == EResult.OK ? EPurchaseResultDetail.NoDetail : EPurchaseResultDetail.CannotRedeemCodeFromClient);
										balanceText = walletResult.Value.BalanceText;
									} else {
										result = EResult.Timeout;
										purchaseResultDetail = EPurchaseResultDetail.Timeout;
									}
								}

								if (items?.Count > 0) {
									response.AppendLine(FormatBotResponse(Strings.FormatBotRedeemWithItems(key, $"{result}/{purchaseResultDetail}{(!string.IsNullOrEmpty(balanceText) ? $"/{balanceText}" : "")}", string.Join(", ", items)), currentBot.BotName));
								} else if (!skipRequest) {
									response.AppendLine(FormatBotResponse(Strings.FormatBotRedeem(key, $"{result}/{purchaseResultDetail}{(!string.IsNullOrEmpty(balanceText) ? $"/{balanceText}" : "")}"), currentBot.BotName));
								}

								switch (purchaseResultDetail) {
									case EPurchaseResultDetail.BadActivationCode:
									case EPurchaseResultDetail.CannotRedeemCodeFromClient:
									case EPurchaseResultDetail.DuplicateActivationCode:
									case EPurchaseResultDetail.NoDetail:
									case EPurchaseResultDetail.Timeout:
										if ((result != EResult.Timeout) && (purchaseResultDetail != EPurchaseResultDetail.Timeout)) {
											unusedKeys.Remove(key);
										}

										// Next key
										key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

										if (purchaseResultDetail == EPurchaseResultDetail.NoDetail) {
											// Next bot (if needed)
											break;
										}

										// Keep current bot
										continue;
									case EPurchaseResultDetail.AccountLocked:
									case EPurchaseResultDetail.AlreadyPurchased:
									case EPurchaseResultDetail.CancelledByUser:
									case EPurchaseResultDetail.DoesNotOwnRequiredApp:
									case EPurchaseResultDetail.NoWallet:
									case EPurchaseResultDetail.RestrictedCountry:
										if (!forward || (keepMissingGames && (purchaseResultDetail != EPurchaseResultDetail.AlreadyPurchased))) {
											// Next key
											key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

											// Next bot (if needed)
											break;
										}

										if (distribute) {
											// Next bot, without changing key
											break;
										}

										items ??= new Dictionary<uint, string>();

										bool alreadyHandled = false;

										foreach (Bot innerBot in Bot.Bots.Where(bot => (bot.Value != currentBot) && (!redeemFlags.HasFlag(ERedeemFlags.SkipInitial) || (bot.Value != Bot)) && !triedBots.Contains(bot.Value) && !rateLimitedBots.Contains(bot.Value) && bot.Value.IsConnectedAndLoggedOn && ((access >= EAccess.Owner) || ((steamID != 0) && (bot.Value.GetAccess(steamID) >= EAccess.Operator))) && ((items.Count == 0) || items.Keys.Any(packageID => !bot.Value.OwnedPackages.ContainsKey(packageID)))).OrderBy(static bot => bot.Key, Bot.BotsComparer).Select(static bot => bot.Value)) {
											CStore_RegisterCDKey_Response? redeemResponse = await innerBot.Actions.RedeemKey(key).ConfigureAwait(false);

											if (redeemResponse == null) {
												response.AppendLine(FormatBotResponse(Strings.FormatBotRedeem(key, $"{EResult.Timeout}/{EPurchaseResultDetail.Timeout}"), innerBot.BotName));

												continue;
											}

											triedBots.Add(innerBot);

											EResult redeemResult = EResult.BadResponse;
											EPurchaseResultDetail redeemPurchaseResult = EPurchaseResultDetail.NoDetail;

											if (redeemResponse.purchase_receipt_info != null) {
												redeemResult = (EResult) redeemResponse.purchase_receipt_info.purchase_status;
												redeemPurchaseResult = (EPurchaseResultDetail) redeemResponse.purchase_result_details;
											}

											switch (redeemPurchaseResult) {
												case EPurchaseResultDetail.BadActivationCode:
												case EPurchaseResultDetail.DuplicateActivationCode:
												case EPurchaseResultDetail.NoDetail:
													// This key is already handled, as we either redeemed it or we're sure it's dupe/invalid
													alreadyHandled = true;

													unusedKeys.Remove(key);

													break;
												case EPurchaseResultDetail.RateLimited:
													rateLimitedBots.Add(innerBot);

													break;
											}

											Dictionary<uint, string>? redeemItems = redeemResponse.purchase_receipt_info?.line_items.Count > 0 ? redeemResponse.purchase_receipt_info.line_items.ToDictionary(static lineItem => lineItem.packageid, static lineItem => lineItem.line_item_description) : null;

											response.AppendLine(FormatBotResponse(redeemItems?.Count > 0 ? Strings.FormatBotRedeemWithItems(key, $"{redeemResult}/{redeemPurchaseResult}", string.Join(", ", redeemItems)) : Strings.FormatBotRedeem(key, $"{redeemResult}/{redeemPurchaseResult}"), innerBot.BotName));

											if (alreadyHandled) {
												break;
											}

											if (redeemItems == null) {
												continue;
											}

											foreach ((uint packageID, string packageName) in redeemItems.Where(item => !items.ContainsKey(item.Key))) {
												items[packageID] = packageName;
											}
										}

										// Next key
										key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

										// Next bot (if needed)
										break;
									case EPurchaseResultDetail.RateLimited:
										rateLimitedBots.Add(currentBot);

										goto case EPurchaseResultDetail.CancelledByUser;
									default:
										ASF.ArchiLogger.LogGenericError(Strings.FormatWarningUnknownValuePleaseReport(nameof(purchaseResultDetail), purchaseResultDetail));

										unusedKeys.Remove(key);

										// Next key
										key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;

										// Next bot (if needed)
										break;
								}
							}
						}

						// We want to change bot in two cases:
						// a) When we have distribution enabled, obviously
						// b) When we're skipping initial bot AND we have forwarding enabled, otherwise we won't get down to other accounts
						if (distribute || (forward && redeemFlags.HasFlag(ERedeemFlags.SkipInitial))) {
							currentBot = botsEnumerator.MoveNext() ? botsEnumerator.Current : null;
						}
					}
				}

				if (key == startingKey) {
					// We ran out of bots to try for this key, so change it to avoid infinite loop, next key
					key = keysEnumerator.MoveNext() ? keysEnumerator.Current : null;
				}
			}
		}

		if (unusedKeys.Count > 0) {
			response.AppendLine(FormatBotResponse(Strings.FormatUnusedKeys(string.Join(", ", unusedKeys))));
		}

		return response.Length > 0 ? response.ToString() : null;
	}

	private static async Task<string?> ResponseRedeem(EAccess access, string botNames, string keysText, ERedeemFlags redeemFlags = ERedeemFlags.None, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(keysText);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseRedeem(GetProxyAccess(bot, access, steamID), keysText, steamID, redeemFlags))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseRedeemPoints(EAccess access, Dictionary<uint, bool> definitionIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if ((definitionIDs == null) || (definitionIDs.Count == 0)) {
			throw new ArgumentNullException(nameof(definitionIDs));
		}

		if (access < EAccess.Operator) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		HashSet<uint> definitionIDsToCheck = definitionIDs.Where(static entry => !entry.Value).Select(static entry => entry.Key).ToHashSet();

		if (definitionIDsToCheck.Count > 0) {
			Dictionary<uint, LoyaltyRewardDefinition>? definitions = await Bot.Actions.GetRewardItems(definitionIDsToCheck).ConfigureAwait(false);

			if (definitions == null) {
				return FormatBotResponse(Strings.FormatWarningFailedWithError(nameof(Bot.Actions.GetRewardItems)));
			}

			foreach (uint definitionID in definitionIDsToCheck) {
				if (!definitions.TryGetValue(definitionID, out LoyaltyRewardDefinition? definition)) {
					return FormatBotResponse(Strings.FormatWarningFailedWithError(definitionID));
				}

				if (definition.point_cost > 0) {
					return FormatBotResponse(Strings.FormatWarningFailedWithError($"{definitionID} {nameof(definition.point_cost)} ({definition.point_cost}) > 0"));
				}
			}
		}

		// We already did more optimized check, therefore we can skip the one in actions
		IList<EResult> results = await Utilities.InParallel(definitionIDs.Keys.Select(definitionID => Bot.Actions.RedeemPoints(definitionID, true))).ConfigureAwait(false);

		int i = 0;

		StringBuilder response = new();

		foreach (uint definitionID in definitionIDs.Keys) {
			response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicense(definitionID, results[i++])));
		}

		return response.Length > 0 ? response.ToString() : null;
	}

	private async Task<string?> ResponseRedeemPoints(EAccess access, string targetDefinitionIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetDefinitionIDs);

		if (access < EAccess.Operator) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		string[] definitions = targetDefinitionIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (definitions.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(definitions)));
		}

		Dictionary<uint, bool> definitionIDs = new(definitions.Length);

		foreach (string definition in definitions) {
			bool forced = false;
			string definitionToParse = definition;

			if (definitionToParse.EndsWith('!')) {
				forced = true;
				definitionToParse = definitionToParse[..^1];
			}

			if (!uint.TryParse(definitionToParse, out uint definitionID) || (definitionID == 0)) {
				return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(definition)));
			}

			definitionIDs[definitionID] = forced;
		}

		return await ResponseRedeemPoints(access, definitionIDs).ConfigureAwait(false);
	}

	private static async Task<string?> ResponseRedeemPoints(EAccess access, string botNames, string targetDefinitionIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetDefinitionIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseRedeemPoints(GetProxyAccess(bot, access, steamID), targetDefinitionIDs))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseRemoveLicense(EAccess access, string query) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(query);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		StringBuilder response = new();

		string[] entries = query.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		foreach (string entry in entries) {
			uint gameID;
			string type;

			int index = entry.IndexOf('/', StringComparison.Ordinal);

			if ((index > 0) && (entry.Length > index + 1)) {
				if (!uint.TryParse(entry[(index + 1)..], out gameID) || (gameID == 0)) {
					response.AppendLine(FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(gameID))));

					continue;
				}

				type = entry[..index];
			} else if (uint.TryParse(entry, out gameID) && (gameID > 0)) {
				type = "SUB";
			} else {
				response.AppendLine(FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(gameID))));

				continue;
			}

			switch (type.ToUpperInvariant()) {
				case "A" or "APP": {
					HashSet<uint>? packageIDs = ASF.GlobalDatabase?.GetPackageIDs(gameID, Bot.OwnedPackages.Keys, 1);

					if (packageIDs is { Count: 0 }) {
						response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicense($"app/{gameID}", EResult.InvalidState)));

						break;
					}

					EResult result = await Bot.Actions.RemoveLicenseApp(gameID).ConfigureAwait(false);

					response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicense($"app/{gameID}", result)));

					break;
				}

				case "S" or "SUB": {
					if (!Bot.OwnedPackages.ContainsKey(gameID)) {
						response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicense($"sub/{gameID}", EResult.InvalidState)));

						break;
					}

					EResult result = await Bot.Actions.RemoveLicensePackage(gameID).ConfigureAwait(false);

					response.AppendLine(FormatBotResponse(Strings.FormatBotAddLicense($"sub/{gameID}", result)));

					break;
				}

				default: {
					response.AppendLine(FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(gameID))));

					continue;
				}
			}
		}

		return response.Length > 0 ? response.ToString() : null;
	}

	private static async Task<string?> ResponseRemoveLicense(EAccess access, string botNames, string query, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(query);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseRemoveLicense(GetProxyAccess(bot, access, steamID), query))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseReset(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		await Bot.CheckOccupationStatus().ConfigureAwait(false);

		return FormatBotResponse(Strings.Done);
	}

	private static async Task<string?> ResponseReset(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseReset(GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private static string? ResponseRestart(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Owner) {
			return null;
		}

		(bool success, string message) = Actions.Restart();

		return FormatStaticResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private string? ResponseResume(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.FamilySharing) {
			return null;
		}

		(bool success, string message) = Bot.Actions.Resume();

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> ResponseResume(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseResume(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseStart(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		(bool success, string message) = Bot.Actions.Start();

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> ResponseStart(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseStart(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseStats(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Owner) {
			return null;
		}

		ushort memoryInMegabytes = (ushort) (GC.GetTotalMemory(false) / 1024 / 1024);
		TimeSpan uptime = DateTime.UtcNow.Subtract(OS.ProcessStartTime);

		return FormatBotResponse(Strings.FormatBotStats(memoryInMegabytes, uptime.ToHumanReadable()));
	}

	private (string? Response, Bot Bot) ResponseStatus(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.FamilySharing) {
			return (null, Bot);
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return (FormatBotResponse(Bot.KeepRunning ? Strings.BotStatusConnecting : Strings.BotStatusNotRunning), Bot);
		}

		if (Bot.PlayingBlocked) {
			return (FormatBotResponse(Strings.BotStatusPlayingNotAvailable), Bot);
		}

		if (Bot.CardsFarmer.Paused) {
			return (FormatBotResponse(Strings.BotStatusPaused), Bot);
		}

		if (Bot.IsAccountLimited) {
			return (FormatBotResponse(Strings.BotStatusLimited), Bot);
		}

		if (Bot.IsAccountLocked) {
			return (FormatBotResponse(Strings.BotStatusLocked), Bot);
		}

		if (!Bot.CardsFarmer.NowFarming || (Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count == 0)) {
			return (FormatBotResponse(Strings.BotStatusNotIdling), Bot);
		}

		if (Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Count > 1) {
			return (FormatBotResponse(Strings.FormatBotStatusIdlingList(string.Join(", ", Bot.CardsFarmer.CurrentGamesFarmingReadOnly.Select(static game => $"{game.AppID} ({game.GameName})")), Bot.CardsFarmer.GamesToFarmReadOnly.Count, Bot.CardsFarmer.GamesToFarmReadOnly.Sum(static game => game.CardsRemaining), Bot.CardsFarmer.TimeRemaining.ToHumanReadable())), Bot);
		}

		Game soloGame = Bot.CardsFarmer.CurrentGamesFarmingReadOnly.First();

		return (FormatBotResponse(Strings.FormatBotStatusIdling(soloGame.AppID, soloGame.GameName, soloGame.CardsRemaining, Bot.CardsFarmer.GamesToFarmReadOnly.Count, Bot.CardsFarmer.GamesToFarmReadOnly.Sum(static game => game.CardsRemaining), Bot.CardsFarmer.TimeRemaining.ToHumanReadable())), Bot);
	}

	private static async Task<string?> ResponseStatus(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<(string? Response, Bot Bot)> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseStatus(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<(string Response, Bot Bot)> validResults = [..results.Where(static result => !string.IsNullOrEmpty(result.Response))!];

		if (validResults.Count == 0) {
			return null;
		}

		HashSet<Bot> botsRunning = validResults.Where(static result => result.Bot.KeepRunning).Select(static result => result.Bot).ToHashSet();

		string extraResponse = Strings.FormatBotStatusOverview(botsRunning.Count, validResults.Count, botsRunning.Sum(static bot => bot.CardsFarmer.GamesToFarmReadOnly.Count), botsRunning.Sum(static bot => bot.CardsFarmer.GamesToFarmReadOnly.Sum(static game => game.CardsRemaining)));

		return string.Join(Environment.NewLine, validResults.Select(static result => result.Response).Union(extraResponse.ToEnumerable()));
	}

	private async Task<string?> ResponseStop(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		(bool success, string message) = await Bot.Actions.Stop().ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> ResponseStop(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseStop(GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseTradingBlacklist(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		return access < EAccess.Master ? null : FormatBotResponse(Bot.BotDatabase.TradingBlacklistSteamIDs.Count == 0 ? Strings.FormatErrorIsEmpty(nameof(Bot.BotDatabase.TradingBlacklistSteamIDs)) : string.Join(", ", Bot.BotDatabase.TradingBlacklistSteamIDs));
	}

	private static async Task<string?> ResponseTradingBlacklist(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseTradingBlacklist(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseTradingBlacklistAdd(EAccess access, string targetSteamIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetSteamIDs);

		if (access < EAccess.Master) {
			return null;
		}

		string[] targets = targetSteamIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (targets.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(targets)));
		}

		HashSet<ulong> targetIDs = [];

		foreach (string target in targets) {
			if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0) || !new SteamID(targetID).IsIndividualAccount) {
				return FormatBotResponse(Strings.FormatErrorParsingObject(nameof(targetID)));
			}

			targetIDs.Add(targetID);
		}

		return FormatBotResponse(Bot.BotDatabase.TradingBlacklistSteamIDs.AddRange(targetIDs) ? Strings.Done : Strings.NothingFound);
	}

	private static async Task<string?> ResponseTradingBlacklistAdd(EAccess access, string botNames, string targetSteamIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetSteamIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseTradingBlacklistAdd(GetProxyAccess(bot, access, steamID), targetSteamIDs)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseTradingBlacklistRemove(EAccess access, string targetSteamIDs) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(targetSteamIDs);

		if (access < EAccess.Master) {
			return null;
		}

		string[] targets = targetSteamIDs.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (targets.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(targets)));
		}

		HashSet<ulong> targetIDs = [];

		foreach (string target in targets) {
			if (!ulong.TryParse(target, out ulong targetID) || (targetID == 0) || !new SteamID(targetID).IsIndividualAccount) {
				return FormatBotResponse(Strings.FormatErrorParsingObject(nameof(targetID)));
			}

			targetIDs.Add(targetID);
		}

		return FormatBotResponse(Bot.BotDatabase.TradingBlacklistSteamIDs.RemoveRange(targetIDs) ? Strings.Done : Strings.NothingFound);
	}

	private static async Task<string?> ResponseTradingBlacklistRemove(EAccess access, string botNames, string targetSteamIDs, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(targetSteamIDs);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseTradingBlacklistRemove(GetProxyAccess(bot, access, steamID), targetSteamIDs)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseTransfer(EAccess access, string botNameTo) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNameTo);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (Bot.BotConfig.TransferableTypes.Count == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(Bot.BotConfig.TransferableTypes)));
		}

		Bot? targetBot = Bot.GetBot(botNameTo);

		if (targetBot == null) {
			return access >= EAccess.Owner ? FormatBotResponse(Strings.FormatBotNotFound(botNameTo)) : null;
		}

		if (!targetBot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.TargetBotNotConnected);
		}

		if (targetBot.SteamID == Bot.SteamID) {
			return FormatBotResponse(Strings.BotSendingTradeToYourself);
		}

		(bool success, string message) = await Bot.Actions.SendInventory(targetSteamID: targetBot.SteamID, filterFunction: item => Bot.BotConfig.TransferableTypes.Contains(item.Type)).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private static async Task<string?> ResponseTransfer(EAccess access, string botNames, string botNameTo, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(botNameTo);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseTransfer(GetProxyAccess(bot, access, steamID), botNameTo))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private async Task<string?> ResponseTransferByRealAppIDs(EAccess access, HashSet<uint> realAppIDs, Bot targetBot, bool exclude) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if ((realAppIDs == null) || (realAppIDs.Count == 0)) {
			throw new ArgumentNullException(nameof(realAppIDs));
		}

		ArgumentNullException.ThrowIfNull(targetBot);

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		if (Bot.BotConfig.TransferableTypes.Count == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(Bot.BotConfig.TransferableTypes)));
		}

		if (!targetBot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.TargetBotNotConnected);
		}

		if (targetBot.SteamID == Bot.SteamID) {
			return FormatBotResponse(Strings.BotSendingTradeToYourself);
		}

		(bool success, string message) = await Bot.Actions.SendInventory(targetSteamID: targetBot.SteamID, filterFunction: item => Bot.BotConfig.TransferableTypes.Contains(item.Type) && (exclude ^ realAppIDs.Contains(item.RealAppID))).ConfigureAwait(false);

		return FormatBotResponse(success ? message : Strings.FormatWarningFailedWithError(message));
	}

	private async Task<string?> ResponseTransferByRealAppIDs(EAccess access, string realAppIDsText, string botNameTo, bool exclude) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(realAppIDsText);
		ArgumentException.ThrowIfNullOrEmpty(botNameTo);

		if (access < EAccess.Master) {
			return null;
		}

		Bot? targetBot = Bot.GetBot(botNameTo);

		if (targetBot == null) {
			return access >= EAccess.Owner ? FormatBotResponse(Strings.FormatBotNotFound(botNameTo)) : null;
		}

		string[] appIDTexts = realAppIDsText.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (appIDTexts.Length == 0) {
			return FormatBotResponse(Strings.FormatErrorIsEmpty(nameof(appIDTexts)));
		}

		HashSet<uint> realAppIDs = [];

		foreach (string appIDText in appIDTexts) {
			if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
				return FormatBotResponse(Strings.FormatErrorIsInvalid(nameof(appID)));
			}

			realAppIDs.Add(appID);
		}

		return await ResponseTransferByRealAppIDs(access, realAppIDs, targetBot, exclude).ConfigureAwait(false);
	}

	private static async Task<string?> ResponseTransferByRealAppIDs(EAccess access, string botNames, string realAppIDsText, string botNameTo, bool exclude, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);
		ArgumentException.ThrowIfNullOrEmpty(realAppIDsText);
		ArgumentException.ThrowIfNullOrEmpty(botNameTo);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		string[] appIDTexts = realAppIDsText.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

		if (appIDTexts.Length == 0) {
			return FormatStaticResponse(Strings.FormatErrorIsEmpty(nameof(appIDTexts)));
		}

		HashSet<uint> realAppIDs = [];

		foreach (string appIDText in appIDTexts) {
			if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
				return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(appID)));
			}

			realAppIDs.Add(appID);
		}

		Bot? targetBot = Bot.GetBot(botNameTo);

		if (targetBot == null) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNameTo)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseTransferByRealAppIDs(GetProxyAccess(bot, access, steamID), realAppIDs, targetBot, exclude))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private string? ResponseUnknown(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		return access >= EAccess.Operator ? FormatBotResponse(Strings.UnknownCommand) : null;
	}

	private async Task<string?> ResponseUnpackBoosters(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		if (!Bot.IsConnectedAndLoggedOn) {
			return FormatBotResponse(Strings.BotNotConnected);
		}

		bool result = await Bot.Actions.UnpackBoosterPacks().ConfigureAwait(false);

		return FormatBotResponse(result ? Strings.Success : Strings.Done);
	}

	private static async Task<string?> ResponseUnpackBoosters(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => bot.Commands.ResponseUnpackBoosters(GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	private static async Task<string?> ResponseUpdate(EAccess access, string? channelText = null) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Owner) {
			return null;
		}

		bool forced = false;
		GlobalConfig.EUpdateChannel channel = ASF.GlobalConfig?.UpdateChannel ?? GlobalConfig.DefaultUpdateChannel;

		if (!string.IsNullOrEmpty(channelText)) {
			if (channelText.EndsWith('!')) {
				forced = true;
				channelText = channelText[..^1];
			}

			if (!Enum.TryParse(channelText, true, out channel) || (channel == GlobalConfig.EUpdateChannel.None)) {
				return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(channelText)));
			}
		}

		(bool success, string? message, Version? version) = await Actions.Update(channel, forced).ConfigureAwait(false);

		return FormatStaticResponse($"{(success ? Strings.Success : Strings.WarningFailed)}{(!string.IsNullOrEmpty(message) ? $" {message}" : version != null ? $" {version}" : "")}");
	}

	private static async Task<string?> ResponseUpdatePlugins(EAccess access, string? channelText = null, string? pluginsText = null) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Owner) {
			return null;
		}

		bool forced = false;
		GlobalConfig.EUpdateChannel? channel = null;

		if (!string.IsNullOrEmpty(channelText)) {
			if (channelText.EndsWith('!')) {
				forced = true;
				channelText = channelText[..^1];
			}

			if (!Enum.TryParse(channelText, true, out GlobalConfig.EUpdateChannel parsedChannel) || (parsedChannel == GlobalConfig.EUpdateChannel.None)) {
				return FormatStaticResponse(Strings.FormatErrorIsInvalid(nameof(channelText)));
			}

			channel = parsedChannel;
		}

		bool success;
		string? message;

		if (!string.IsNullOrEmpty(pluginsText)) {
			string[] plugins = pluginsText.Split(SharedInfo.ListElementSeparators, StringSplitOptions.RemoveEmptyEntries);

			if (plugins.Length == 0) {
				return FormatStaticResponse(Strings.FormatErrorIsEmpty(nameof(plugins)));
			}

			(success, message) = await Actions.UpdatePlugins(channel, plugins, forced).ConfigureAwait(false);
		} else {
			(success, message) = await Actions.UpdatePlugins(channel, forced: forced).ConfigureAwait(false);
		}

		return FormatStaticResponse($"{(success ? Strings.Success : Strings.WarningFailed)}{(!string.IsNullOrEmpty(message) ? $" {message}" : "")}");
	}

	private string? ResponseVersion(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		return access >= EAccess.FamilySharing ? FormatBotResponse(Strings.FormatBotVersion(SharedInfo.ASF, SharedInfo.Version)) : null;
	}

	private string? ResponseWalletBalance(EAccess access) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if (access < EAccess.Master) {
			return null;
		}

		return !Bot.IsConnectedAndLoggedOn ? FormatBotResponse(Strings.BotNotConnected) : FormatBotResponse(Bot.WalletCurrency != ECurrencyCode.Invalid ? Strings.FormatBotWalletBalance($"{Bot.WalletBalance / 100.0}{(Bot.WalletBalanceDelayed > 0 ? $" ({(Bot.WalletBalance + Bot.WalletBalanceDelayed) / 100.0})" : "")}", Bot.WalletCurrency.ToString()) : Strings.BotHasNoWallet);
	}

	private static async Task<string?> ResponseWalletBalance(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? FormatStaticResponse(Strings.FormatBotNotFound(botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.Commands.ResponseWalletBalance(GetProxyAccess(bot, access, steamID))))).ConfigureAwait(false);

		List<string> responses = [..results.Where(static result => !string.IsNullOrEmpty(result))!];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	[Flags]
	private enum ERedeemFlags : ushort {
		None = 0,
		Validate = 1,
		ForceForwarding = 2,
		SkipForwarding = 4,
		ForceDistributing = 8,
		SkipDistributing = 16,
		SkipInitial = 32,
		ForceKeepMissingGames = 64,
		SkipKeepMissingGames = 128,
		ForceAssumeWalletKeyOnBadActivationCode = 256,
		SkipAssumeWalletKeyOnBadActivationCode = 512
	}
}
