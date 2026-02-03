using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;

namespace Groups;

public static class Map
{
	private static Sprite groupMapPlayerIcon = null!;
	private static Sprite groupMapPingIcon = null!;
	private static readonly ConditionalWeakTable<Chat.WorldTextInstance, object> groupPingTexts = new();
	private static readonly Color defaultColor = new(1f, 0.7176471f, 0.3602941f);

	public static void Init()
	{
		groupMapPlayerIcon = Helper.loadSprite("groupPlayerIcon.png", 64, 64);
		groupMapPingIcon = Helper.loadSprite("groupMapPingIcon.png", 64, 64);

		if (groupMapPlayerIcon != null && groupMapPingIcon != null)
		{
			UpdateMapPinColor();
		}
		else
		{
			Debug.LogError("Failed to load map ping icons - sprites are null");
		}
	}

	[HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.ShowHud))]
	public class ColorNames
	{
		private static void Postfix(Character c, Dictionary<Character, EnemyHud.HudData> ___m_huds)
		{
			if (c is not Player player)
			{
				return;
			}

			GameObject hudBase = ___m_huds[c].m_gui;

			TextMeshProUGUI playerName = hudBase.transform.Find("Name").GetComponent<TextMeshProUGUI>();

			playerName.color = Groups.ownGroup != null && Groups.ownGroup.playerStates.ContainsKey(PlayerReference.fromPlayer(player)) ? Groups.friendlyNameColor.Value : defaultColor;
		}
	}

	[HarmonyPatch(typeof(Chat), nameof(Chat.SendPing))]
	private static class RestrictPingsToGroupOnModifierHeld
	{
		[UsedImplicitly]
		private static bool RestrictBroadcast(ZRoutedRpc instance, long targetPeerId, string methodName, params object[] parameters)
		{
			if (Groups.ownGroup is not null && targetPeerId == ZRoutedRpc.Everybody && Groups.groupPingHotkey.Value.IsPressed())
			{
				foreach (PlayerReference playerReference in Groups.ownGroup.playerStates.Keys)
				{
					instance.InvokeRoutedRPC(playerReference.peerId, "Groups MapPing", parameters);
				}
				return true;
			}

			return false;

		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructionsEnumerable, ILGenerator ilg)
		{
			MethodInfo routedRPC = AccessTools.DeclaredMethod(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), new[] { typeof(long), typeof(string), typeof(object[]) });
			MethodInfo routedRPCInstance = AccessTools.DeclaredPropertyGetter(typeof(ZRoutedRpc), nameof(ZRoutedRpc.instance));

			List<CodeInstruction> instructions = instructionsEnumerable.ToList();

			int methodEndIndex = instructions.FindIndex(i => i.Calls(routedRPC));
			int methodStartIndex = instructions.FindLastIndex(methodEndIndex, i => i.Calls(routedRPCInstance));

			Label skip = ilg.DefineLabel();
			instructions[methodEndIndex + 1].labels.Add(skip);

			// Repeat all instructions for method call, then skip original if restricted
			instructions.InsertRange(methodStartIndex, instructions.Skip(methodStartIndex).Take(methodEndIndex - methodStartIndex).Concat(new[]
			{
				new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(RestrictPingsToGroupOnModifierHeld), nameof(RestrictBroadcast))),
				new CodeInstruction(OpCodes.Brtrue, skip),
			}).ToArray());

			return instructions;
		}
	}

	[HarmonyPatch(typeof(Chat), nameof(Chat.RPC_ChatMessage))]
	private class ClearGroupPing
	{
		public static void Prefix(Chat __instance, long sender)
		{
			if (__instance.FindExistingWorldText(sender) is { } text && groupPingTexts.Remove(text) && Minimap.instance)
			{
				for (int i = 0; i < Minimap.instance.m_tempShouts.Count; ++i)
				{
					Minimap.PinData pingPin = Minimap.instance.m_pingPins[i];
					Chat.WorldTextInstance tempShout = Minimap.instance.m_tempShouts[i];
					if (tempShout == text)
					{
						pingPin.m_icon = Minimap.instance.GetSprite(Minimap.PinType.Ping);
						if (pingPin.m_iconElement)
						{
							pingPin.m_iconElement.sprite = pingPin.m_icon;
						}
					}
				}
			}
		}
	}

	public static void onMapPing(long senderId, Vector3 position, int type, UserInfo name, string text)
	{
		Chat.instance.RPC_ChatMessage(senderId, position, type, name, text);
		Chat.WorldTextInstance worldText = Chat.instance.FindExistingWorldText(senderId);
		worldText.m_textMeshField.color = Groups.friendlyNameColor.Value;
		groupPingTexts.Add(worldText, Array.Empty<object>());
	}

	public static void UpdateMapPinColor()
	{
		if (groupMapPlayerIcon == null || groupMapPingIcon == null)
		{
			Debug.LogWarning("Cannot update map pin color - sprites not initialized");
			return;
		}
		
		Texture2D playerIconTexture = Helper.loadTexture("groupPlayerIcon.png");
		if (playerIconTexture != null)
		{
			Color[]? pixels = playerIconTexture.GetPixels();
			for (int i = 0; i < pixels.Length; ++i)
			{
				if (pixels[i].r > 0.5 && pixels[i].b < 0.5 && pixels[i].g < 0.5)
				{
					pixels[i] = Groups.friendlyNameColor.Value;
				}
			}
			groupMapPlayerIcon.texture.SetPixels(pixels);
			groupMapPlayerIcon.texture.Apply();
		}

		Texture2D pingIconTexture = Helper.loadTexture("groupMapPingIcon.png");
		if (pingIconTexture != null)
		{
			Color[]? pixels = pingIconTexture.GetPixels();
			for (int i = 0; i < pixels.Length; ++i)
			{
				if (pixels[i].r > 0.5 && pixels[i].b < 0.5 && pixels[i].g < 0.5)
				{
					pixels[i].b = Groups.friendlyNameColor.Value.b;
					pixels[i].g = Groups.friendlyNameColor.Value.g;
					pixels[i].r = Groups.friendlyNameColor.Value.r;
				}
			}
			pingIconTexture.SetPixels(pixels);
			pingIconTexture.Apply();
		}
	}

	[HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePlayerPins))]
	private class ChangeGroupMemberPin
	{
		private static void Postfix(Minimap __instance)
		{
			for (int index = 0; index < __instance.m_tempPlayerInfo.Count; ++index)
			{
				Minimap.PinData playerPin = __instance.m_playerPins[index];
				ZNet.PlayerInfo playerInfo = __instance.m_tempPlayerInfo[index];
				if (playerPin.m_name == playerInfo.m_name)
				{
					bool changed = false;
					if (Groups.ownGroup?.playerStates.ContainsKey(PlayerReference.fromPlayerInfo(playerInfo)) == true)
					{
						playerPin.m_icon = groupMapPlayerIcon;
						changed = true;
					}
					else if (playerPin.m_icon == groupMapPlayerIcon)
					{
						playerPin.m_icon = __instance.GetSprite(Minimap.PinType.Player);
						changed = true;
					}
					if (changed && playerPin.m_iconElement)
					{
						playerPin.m_iconElement.sprite = playerPin.m_icon;
					}
				}
			}
		}
	}

	[HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePingPins))]
	private class ChangeGroupMemberPing
	{
		private static void Postfix(Minimap __instance)
		{
			for (int i = 0; i < __instance.m_tempShouts.Count; ++i)
			{
				Minimap.PinData pingPin = __instance.m_pingPins[i];
				Chat.WorldTextInstance tempShout = __instance.m_tempShouts[i];
				if (groupPingTexts.TryGetValue(tempShout, out _))
				{
					pingPin.m_icon = groupMapPingIcon;
					if (pingPin.m_iconElement)
					{
						pingPin.m_iconElement.sprite = pingPin.m_icon;
					}
				}
			}
		}
	}
}
