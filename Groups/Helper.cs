using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Groups;

public static class Helper
{
	private static byte[] ReadEmbeddedFileBytes(string name)
	{
		using MemoryStream stream = new();
		Assembly.GetExecutingAssembly().GetManifestResourceStream("Groups." + name)?.CopyTo(stream);
		return stream.ToArray();
	}

	public static Texture2D loadTexture(string name)
	{
		byte[] data = ReadEmbeddedFileBytes("icons." + name);
		if (data == null || data.Length == 0)
		{
			Debug.LogError($"Failed to load embedded texture: {name}");
			return null;
		}
		
		// Create texture - size will be adjusted by LoadImage
		Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
		
		try
		{
			// Try to use LoadImage extension method from ImageConversion static type
			// LoadImage is a static extension method in UnityEngine.ImageConversion
			var imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
			
			if (imageConversionType != null)
			{
				// Try the standard LoadImage signature with bool parameter
				MethodInfo loadImageMethod = imageConversionType.GetMethod(
					"LoadImage",
					BindingFlags.Static | BindingFlags.Public,
					null,
					new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) },
					null
				) ?? imageConversionType.GetMethod(
					"LoadImage",
					BindingFlags.Static | BindingFlags.Public,
					null,
					new Type[] { typeof(Texture2D), typeof(byte[]) },
					null
				);
				
				if (loadImageMethod != null)
				{
					try
					{
						var parameters = loadImageMethod.GetParameters();
						object result;
						if (parameters.Length == 3)
							result = loadImageMethod.Invoke(null, new object[] { texture, data, false });
						else
							result = loadImageMethod.Invoke(null, new object[] { texture, data });
						
						if (result is bool success && success)
						{
							return texture;
						}
					}
					catch (Exception invokeEx)
					{
						Debug.LogWarning($"Failed to invoke LoadImage for {name}: {invokeEx.InnerException?.Message}");
					}
				}
			}
			
			Debug.LogWarning($"LoadImage not available for {name} - ImageConversionModule not accessible. Using placeholder.");
		}
		catch (Exception ex)
		{
			Debug.LogError($"Exception loading texture {name}: {ex.GetType().Name}: {ex.Message}");
		}
		
		return texture;
	}

	public static Sprite loadSprite(string name, int width, int height)
	{
		Texture2D tex = loadTexture(name);
		if (tex == null)
		{
			Debug.LogError($"Failed to load texture for sprite {name}");
			return null;
		}
		if (tex.width >= width && tex.height >= height)
		{
			return Sprite.Create(tex, new Rect(0, 0, width, height), Vector2.zero);
		}
		else
		{
			Debug.LogWarning($"Texture {name} is too small ({tex.width}x{tex.height}), expected at least ({width}x{height})");
			return null;
		}
	}
}




