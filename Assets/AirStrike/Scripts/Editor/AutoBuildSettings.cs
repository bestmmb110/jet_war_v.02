#if UNITY_EDITOR
using System.Linq;
using UnityEditor;

// Makes sure "Classic" is the FIRST scene in Build Settings, so the game
// starts directly in the battle (no main menu), both in builds and in Play mode.
[InitializeOnLoad]
public static class AutoBuildSettings
{
	const string ClassicPath = "Assets/AirStrike/Demo/Classic.unity";

	static AutoBuildSettings()
	{
		var scenes = EditorBuildSettings.scenes.ToList();
		if (scenes.Count > 0 && scenes[0].path == ClassicPath && scenes[0].enabled)
			return;

		scenes.RemoveAll(s => s.path == ClassicPath);
		scenes.Insert(0, new EditorBuildSettingsScene(ClassicPath, true));
		EditorBuildSettings.scenes = scenes.ToArray();
	}
}
#endif
