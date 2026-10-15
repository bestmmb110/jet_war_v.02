using HWRcomponent;
using UnityEngine;
using UnityEngine.SceneManagement;

// Standalone add-on for Air Strike Starter Kit. It does NOT modify any original file or scene.
// Put it anywhere inside Assets/ (for example next to LiveBattleStart.cs).
//
// What it does:
//  * Classic  : reads the working radar settings of the Classic scene (textures, tags, size ...)
//               and remembers them.
//  * Modern / Invasion : finds the player's RadarSystem, switches it on (enabled + Show) and, if it is
//               not configured, gives it the Classic settings. If Classic settings are not known yet
//               (Modern is played first), it uses a built-in radar made of generated textures.
public class RadarModeFix : MonoBehaviour
{
	const string ReferenceScene = "Classic";
	static readonly string[] FixScenes = { "Modern", "Invasion" };

	// ---- fallback radar (used only when the scene radar is not configured and Classic is not cached yet)
	const float FallbackSize = 220f;
	const float FallbackScale = 2f;          // 1 pixel = 2 world units
	const float FallbackDistance = 100f;     // detection range = Distance * Scale world units
	const Alignment FallbackAlignment = Alignment.RightBot;
	static readonly Vector2 FallbackOffset = new Vector2 (-10f, -10f);
	static readonly string[] FallbackTags = { "Enemy" };

	// ---- cache of the Classic radar
	static bool cached;
	static float cSize, cDistance, cAlpha, cScale;
	static Texture2D[] cNav;
	static string[] cTags;
	static Texture2D cCompass, cBG;
	static Vector2 cOffset;
	static Alignment cAlign;
	static bool cRotation;
	static Color cColor;

	static Texture2D dotTex, bgTex;

	enum Mode { None, Capture, Fix }

	static bool bootDone;
	Mode mode;
	bool done;
	RadarSystem radar;

	[RuntimeInitializeOnLoadMethod (RuntimeInitializeLoadType.AfterSceneLoad)]
	static void Boot ()
	{
		if (bootDone)
			return;
		bootDone = true;
		GameObject go = new GameObject ("RadarModeFix");
		DontDestroyOnLoad (go);
		RadarModeFix host = go.AddComponent<RadarModeFix> ();
		host.OnSceneLoaded (SceneManager.GetActiveScene (), LoadSceneMode.Single);
		SceneManager.sceneLoaded += host.OnSceneLoaded;
	}

	void OnDestroy ()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	void OnSceneLoaded (Scene scene, LoadSceneMode loadMode)
	{
		done = false;
		radar = null;
		mode = Mode.None;
		if (scene.name == ReferenceScene)
			mode = Mode.Capture;
		else if (System.Array.IndexOf (FixScenes, scene.name) >= 0)
			mode = Mode.Fix;
	}

	void Update ()
	{
		if (mode == Mode.None)
			return;

		if (done) {
			// keep it visible during the whole game
			if (mode == Mode.Fix && radar) {
				radar.enabled = true;
				radar.Show = true;
			}
			return;
		}

		PlayerController pc = Object.FindFirstObjectByType<PlayerController> ();
		if (!pc)
			return; // player not spawned yet, try again next frame

		radar = pc.GetComponent<RadarSystem> ();
		if (!radar)
			radar = Object.FindFirstObjectByType<RadarSystem> ();

		if (mode == Mode.Capture) {
			if (radar && IsConfigured (radar))
				Capture (radar);
			done = true;
			return;
		}

		// Mode.Fix
		if (!radar)
			radar = pc.gameObject.AddComponent<RadarSystem> ();

		radar.enabled = true;
		radar.Show = true;
		radar.Player = pc.gameObject;

		if (!IsConfigured (radar)) {
			if (cached)
				ApplyCached (radar);
			else
				ApplyFallback (radar);
		}
		done = true;
	}

	// ------------------------------------------------------------ helpers

	static bool IsConfigured (RadarSystem r)
	{
		if (r.EnemyTag == null || r.EnemyTag.Length == 0)
			return false;
		if (r.Navtexture == null || r.Navtexture.Length < r.EnemyTag.Length)
			return false;
		for (int i = 0; i < r.EnemyTag.Length; i++) {
			if (string.IsNullOrEmpty (r.EnemyTag [i]) || !r.Navtexture [i])
				return false;
		}
		return r.Size > 0f;
	}

	static void Capture (RadarSystem r)
	{
		cSize = r.Size;
		cDistance = r.Distance;
		cAlpha = r.Alpha;
		cScale = r.Scale;
		cNav = (Texture2D[])r.Navtexture.Clone ();
		cTags = (string[])r.EnemyTag.Clone ();
		cCompass = r.NavCompass;
		cBG = r.NavBG;
		cOffset = r.PositionOffset;
		cAlign = r.PositionAlignment;
		cRotation = r.MapRotation;
		cColor = r.ColorMult;
		cached = true;
	}

	static void ApplyCached (RadarSystem r)
	{
		r.Size = cSize;
		r.Distance = cDistance;
		r.Alpha = cAlpha;
		r.Scale = cScale;
		r.Navtexture = (Texture2D[])cNav.Clone ();
		r.EnemyTag = (string[])cTags.Clone ();
		r.NavCompass = cCompass;
		r.NavBG = cBG;
		r.PositionOffset = cOffset;
		r.PositionAlignment = cAlign;
		r.MapRotation = cRotation;
		r.ColorMult = cColor;
	}

	static void ApplyFallback (RadarSystem r)
	{
		BuildTextures ();
		r.Size = FallbackSize;
		r.Distance = FallbackDistance;
		r.Alpha = 0.9f;
		r.Scale = FallbackScale;
		r.Navtexture = new Texture2D[] { dotTex };
		r.EnemyTag = (string[])FallbackTags.Clone ();
		r.NavCompass = null;
		r.NavBG = bgTex;
		r.PositionOffset = FallbackOffset;
		r.PositionAlignment = FallbackAlignment;
		r.MapRotation = false;
		r.ColorMult = Color.white;
	}

	static void BuildTextures ()
	{
		if (dotTex && bgTex)
			return;

		// enemy dot: red filled circle
		int d = 14;
		dotTex = new Texture2D (d, d, TextureFormat.RGBA32, false);
		float c = (d - 1) / 2f;
		for (int y = 0; y < d; y++) {
			for (int x = 0; x < d; x++) {
				float dist = Vector2.Distance (new Vector2 (x, y), new Vector2 (c, c));
				float a = Mathf.Clamp01 (c + 0.5f - dist);
				dotTex.SetPixel (x, y, new Color (1f, 0.15f, 0.1f, a));
			}
		}
		dotTex.Apply ();
		dotTex.wrapMode = TextureWrapMode.Clamp;

		// background: dark green disc, bright ring, cross lines and a mid ring
		int s = 256;
		bgTex = new Texture2D (s, s, TextureFormat.RGBA32, true);
		float m = (s - 1) / 2f;
		for (int y = 0; y < s; y++) {
			for (int x = 0; x < s; x++) {
				float dist = Vector2.Distance (new Vector2 (x, y), new Vector2 (m, m));
				Color col = new Color (0f, 0f, 0f, 0f);
				if (dist <= m) {
					col = new Color (0.02f, 0.18f, 0.06f, 0.35f);
					if (dist > m - 3f)
						col = new Color (0.2f, 1f, 0.35f, 0.9f);
					else if (Mathf.Abs (dist - m / 2f) < 1.2f)
						col = new Color (0.2f, 1f, 0.35f, 0.35f);
					else if (Mathf.Abs (x - m) < 1f || Mathf.Abs (y - m) < 1f)
						col = new Color (0.2f, 1f, 0.35f, 0.35f);
					if (dist < 4f)
						col = new Color (0.3f, 0.8f, 1f, 1f); // player in the center
				}
				bgTex.SetPixel (x, y, col);
			}
		}
		bgTex.Apply ();
		bgTex.wrapMode = TextureWrapMode.Clamp;
	}
}
