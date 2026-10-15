using UnityEngine;
using UnityEngine.SceneManagement;

// NEW add-on (put next to SpaceSpeedControl.cs). No original file is modified.
//
//  * Starts by itself and also creates ArduinoSerialBridge automatically (no manual GameObject needed).
//  * While the Arduino is connected: SpaceSpeedControl is switched off and the speed potentiometer
//    (A2) sets the player's speed: pot at minimum = 300 km/h, pot at maximum = 3500 km/h.
//  * If the Arduino is not connected / unplugged: SpaceSpeedControl is switched back on,
//    so the keyboard Space control keeps working exactly as before.
//
// Same technique as SpaceSpeedControl: only the LENGTH of the Rigidbody velocity is replaced
// after the original FlightSystem has run (execution order 100).
[DefaultExecutionOrder (101)]
public class PotSpeedControl : MonoBehaviour
{
	const float CruiseKmh = 300f;
	const float MaxKmh = 3500f;
	const float PotDeadLow = 0.03f;        // pot below this = cruise speed
	const float PotDeadHigh = 0.97f;       // pot above this = max speed
	const float AccelKmhPerSec = 1600f;    // how fast speed follows the pot (smooth, max ~2 s full range)
	const bool ShowSpeedLabel = true;
	const string SkipScene = "Mainmenu";   // live auto-battle scene: no player control there
	const float KmhToMs = 1f / 3.6f;

	static bool bootDone;

	Rigidbody body;
	PlayerController player;
	SpaceSpeedControl spaceControl;
	float nextFind;
	float speedKmh = CruiseKmh;
	GUIStyle label;

	[RuntimeInitializeOnLoadMethod (RuntimeInitializeLoadType.AfterSceneLoad)]
	static void Boot ()
	{
		if (bootDone)
			return;
		bootDone = true;
		GameObject go = new GameObject ("PotSpeedControl");
		DontDestroyOnLoad (go);
		go.AddComponent<PotSpeedControl> ();
		if (Object.FindFirstObjectByType<ArduinoSerialBridge> () == null)
			go.AddComponent<ArduinoSerialBridge> ();   // serial reader + keyboard/mouse injection
	}

	void OnEnable ()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	void OnDisable ()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	void OnSceneLoaded (Scene scene, LoadSceneMode mode)
	{
		player = null;
		body = null;
		nextFind = 0f;
		speedKmh = CruiseKmh;
	}

	bool SceneActive ()
	{
		return SceneManager.GetActiveScene ().name != SkipScene;
	}

	void FindPlayer ()
	{
		if (Time.unscaledTime < nextFind)
			return;
		nextFind = Time.unscaledTime + 0.5f;
		player = Object.FindFirstObjectByType<PlayerController> ();
		if (player) {
			body = player.GetComponent<Rigidbody> ();
			if (body && !body.isKinematic)
				body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		}
	}

	bool PotMode ()
	{
		return ArduinoInput.Connected;
	}

	void Update ()
	{
		// hand over control between Space and pot
		if (!spaceControl)
			spaceControl = Object.FindFirstObjectByType<SpaceSpeedControl> ();
		if (spaceControl && spaceControl.enabled == PotMode ())
			spaceControl.enabled = !PotMode ();

		if (!PotMode () || !SceneActive ())
			return;

		if (!player || !body)
			FindPlayer ();
		if (!player || !player.Active)
			return;

		float p = Mathf.InverseLerp (PotDeadLow, PotDeadHigh, ArduinoInput.Pot01);
		float target = Mathf.Lerp (CruiseKmh, MaxKmh, p);
		speedKmh = Mathf.MoveTowards (speedKmh, target, AccelKmhPerSec * Time.deltaTime);
	}

	void FixedUpdate ()
	{
		if (!PotMode () || !SceneActive () || !player || !body || !player.Active)
			return;

		Vector3 v = body.linearVelocity;
		Vector3 dir = v.sqrMagnitude > 0.0001f ? v.normalized : body.rotation * Vector3.forward;
		body.linearVelocity = dir * (speedKmh * KmhToMs);
	}

	void OnGUI ()
	{
		if (!ShowSpeedLabel || !PotMode () || !SceneActive () || !player)
			return;
		if (label == null) {
			label = new GUIStyle (GUI.skin.label);
			label.fontSize = 18;
			label.fontStyle = FontStyle.Bold;
			label.alignment = TextAnchor.MiddleCenter;
			label.normal.textColor = Color.white;
		}
		GUI.Label (new Rect (Screen.width / 2f - 150f, Screen.height - 50f, 300f, 30f), "SPEED  " + Mathf.RoundToInt (speedKmh) + " km/h", label);
	}
}
