using UnityEngine;
using UnityEngine.SceneManagement;

// Standalone add-on for Air Strike Starter Kit. It does NOT modify any original file or scene.
// Put it anywhere inside Assets/ (for example next to LiveBattleStart.cs). It starts by itself.
//
// Space-key speed control for the PLAYER jet (AI jets are not touched):
//  * Cruise speed      : 300 km/h (default speed of the jet).
//  * Max speed         : 3,500 km/h.
//  * HOLD Space        : smooth, gradual acceleration; cruise -> max takes 8 seconds of holding.
//  * RELEASE Space     : the speed stays at whatever level it reached.
//  * DOUBLE TAP Space  : (tap, release, tap, release within 3 seconds) -> speed rises quickly
//                        to max speed within 2 seconds.
//  * SINGLE TAP Space  : (one tap, no second tap within 3 seconds) -> speed returns to cruise.
//
// The speed is applied to the Rigidbody velocity right after the original FlightSystem has set it,
// so the original scripts stay untouched. 1 Unity unit is treated as 1 meter.
[DefaultExecutionOrder (100)]
public class SpaceSpeedControl : MonoBehaviour
{
	// ------------------------------------------------ settings (change here if you want)
	const float CruiseKmh = 300f;
	const float MaxKmh = 3500f;
	const float HoldSecondsToMax = 8f;    // holding Space: cruise -> max in this many seconds
	const float BoostSeconds = 2f;        // double tap: reach max in this many seconds
	const float ReturnSeconds = 3f;       // single tap: time of the smooth return to cruise
	const float TapMaxHold = 0.25f;       // a press shorter than this is a "tap", longer is a "hold"
	const float TapWindow = 3f;           // time window used to detect single / double tap
	const KeyCode Key = KeyCode.Space;
	const bool ShowSpeedLabel = true;
	const string SkipScene = "Mainmenu";  // live auto-battle scene: no player control there

	const float KmhToMs = 1f / 3.6f;

	enum Mode { Idle, Holding, Boosting, Returning }

	static bool bootDone;

	Rigidbody body;
	PlayerController player;
	float nextFind;

	float speed = CruiseKmh * KmhToMs;    // current speed (m/s)
	Mode mode = Mode.Idle;
	float blendFrom, blendT;

	bool pressing, held, tapPending;
	float pressTime, firstTapTime;

	GUIStyle label;

	[RuntimeInitializeOnLoadMethod (RuntimeInitializeLoadType.AfterSceneLoad)]
	static void Boot ()
	{
		if (bootDone)
			return;
		bootDone = true;
		GameObject go = new GameObject ("SpaceSpeedControl");
		DontDestroyOnLoad (go);
		go.AddComponent<SpaceSpeedControl> ();
	}

	void OnEnable ()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	void OnDisable ()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	void OnSceneLoaded (Scene scene, LoadSceneMode loadMode)
	{
		// every new scene starts at cruise speed with a clean state
		player = null;
		body = null;
		nextFind = 0f;
		speed = CruiseKmh * KmhToMs;
		mode = Mode.Idle;
		pressing = held = tapPending = false;
	}

	bool Enabled ()
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
			if (body && !body.isKinematic) {
				// at very high speed a normal collider can pass through objects between two physics steps
				body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
			}
		}
	}

	// ------------------------------------------------ input and speed logic

	void Update ()
	{
		if (!Enabled ())
			return;

		if (!player || !body)
			FindPlayer ();
		if (!player || !player.Active)
			return;

		float now = Time.time;
		float dt = Time.deltaTime;

		if (Input.GetKeyDown (Key)) {
			pressing = true;
			held = false;
			pressTime = now;
		}

		// a press longer than TapMaxHold is a "hold" -> gradual acceleration
		if (pressing && !held && Input.GetKey (Key) && now - pressTime >= TapMaxHold) {
			held = true;
			tapPending = false;
			mode = Mode.Holding;
		}

		if (Input.GetKeyUp (Key) && pressing) {
			pressing = false;
			if (held) {
				// released: keep the current speed level
				held = false;
				mode = Mode.Idle;
			} else if (tapPending && now - firstTapTime <= TapWindow) {
				// second quick tap -> fast boost to max speed
				tapPending = false;
				StartBlend (Mode.Boosting);
			} else {
				// first tap -> wait for a possible second tap
				tapPending = true;
				firstTapTime = now;
			}
		}

		// only one tap within the window -> back to cruise
		if (tapPending && now - firstTapTime > TapWindow) {
			tapPending = false;
			StartBlend (Mode.Returning);
		}

		float max = MaxKmh * KmhToMs;
		float cruise = CruiseKmh * KmhToMs;

		switch (mode) {
		case Mode.Holding:
			speed = Mathf.MoveTowards (speed, max, (max - cruise) / HoldSecondsToMax * dt);
			break;
		case Mode.Boosting:
			blendT += dt / BoostSeconds;
			speed = Mathf.Lerp (blendFrom, max, Mathf.SmoothStep (0f, 1f, blendT));
			if (blendT >= 1f)
				mode = Mode.Idle;
			break;
		case Mode.Returning:
			blendT += dt / ReturnSeconds;
			speed = Mathf.Lerp (blendFrom, cruise, Mathf.SmoothStep (0f, 1f, blendT));
			if (blendT >= 1f)
				mode = Mode.Idle;
			break;
		}
	}

	void StartBlend (Mode m)
	{
		mode = m;
		blendFrom = speed;
		blendT = 0f;
	}

	// runs after FlightSystem.FixedUpdate (execution order 100): only the length of the velocity is replaced,
	// the direction still comes from the original flight code.
	void FixedUpdate ()
	{
		if (!Enabled () || !player || !body || !player.Active)
			return;

		Vector3 v = body.linearVelocity;
		Vector3 dir = v.sqrMagnitude > 0.0001f ? v.normalized : body.rotation * Vector3.forward;
		body.linearVelocity = dir * speed;
	}

	void OnGUI ()
	{
		if (!ShowSpeedLabel || !Enabled () || !player)
			return;
		if (label == null) {
			label = new GUIStyle (GUI.skin.label);
			label.fontSize = 18;
			label.fontStyle = FontStyle.Bold;
			label.alignment = TextAnchor.MiddleCenter;
			label.normal.textColor = Color.white;
		}
		float kmh = speed / KmhToMs;
		GUI.Label (new Rect (Screen.width / 2f - 150f, Screen.height - 50f, 300f, 30f), "SPEED  " + Mathf.RoundToInt (kmh) + " km/h", label);
	}
}
