using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Runtime bootstrap and game flow for the native Unity port of Shadow Ruins.</summary>
[DefaultExecutionOrder(-100)]
public class ShadowRuinsGame : MonoBehaviour
{
    public static ShadowRuinsGame Instance { get; private set; }

    public enum ScreenMode { MainMenu, Playing, Paused, Options, Loading, Victory }
    public ScreenMode Mode { get; private set; } = ScreenMode.MainMenu;

    [Serializable]
    public class GameSettings
    {
        public int graphicsPreset = 1;       // 0 Low, 1 Balanced, 2 High
        public int realism = 0;              // 0 Classic, 1 Enhanced, 2 Ultra Realistic
        public bool shadows = true;
        public bool spellEffects = true;
        public bool invertMouseY = false;
        public bool musicEnabled = true;
        public bool showFps = false;
        public float mouseSensitivity = 1.0f;
        public float musicVolume = 0.5f;
        public float viewDistance = 100.0f;
        public int targetFps = 144;
    }

    public GameSettings Settings { get; private set; } = new GameSettings();
    public ShadowRuinsPlayer Player { get; private set; }
    public int LevelNumber { get; private set; } = 1;
    public int SigilsCollected { get; private set; }
    public int SigilsRequired { get; private set; } = 3;
    public float Mana { get; private set; } = 100f;
    public string StatusMessage { get; private set; } = "Find the three hidden sigils and reach the gate.";

    private Light sun;
    private Camera menuCamera;
    private AudioListener menuAudioListener;
    private GameObject menuCastle;
    private Transform levelRoot;
    private AudioSource musicSource;
    private readonly List<GameObject> sigils = new List<GameObject>();
    private readonly List<GameObject> sigilPlatforms = new List<GameObject>();
    private readonly List<GameObject> sigilLabels = new List<GameObject>();
    private readonly List<bool> sigilGot = new List<bool>();
    private float revealTimer;
    private float revealCooldown;
    private sealed class GlyphTrial
    {
        public GameObject marker;
        public Renderer renderer;
        public string symbol;
        public int sequenceIndex;
        public bool solved;
        public int platformIndex;
    }
    private sealed class BridgeTrial
    {
        public GameObject socket;
        public GameObject bridge;
        public int sourceIndex;
        public int gapIndex;
        public int destinationIndex;
        public bool active;
        public bool entered;
        public bool completed;
    }
    private readonly List<GlyphTrial> glyphTrials = new List<GlyphTrial>();
    private readonly List<BridgeTrial> bridgeTrials = new List<BridgeTrial>();
    private GameObject pastSeal, presentSeal, echoVisual, highCrystal, chronoWarning, portalMarker;
    private Renderer pastSealRenderer, presentSealRenderer, highCrystalRenderer, chronoWarningRenderer, portalRenderer;
    private bool echoPuzzleSolved, echoAnchorActive, highCrystalCollected, chronoPuzzleSolved, chronoStarted;
    private bool requiresCrystal, requiresChrono;
    private float echoTimer, chronoCollapseTimer, chronoBoundTimer, crystalSpin;
    private int glyphSequenceIndex, puzzleProfileIndex;
    private GameObject chronoPlatform, activeStoneStep;
    private bool stoneStepLanded, crystalJumpArmed;
    private Vector3 pastSealPosition, presentSealPosition;
    private readonly List<string> currentGlyphSequence = new List<string>();
    private readonly List<string> songNames = new List<string> { "Citadel March", "Forgotten Halls", "Arcane Pursuit", "Crystal Sanctum", "Final Guardian" };
    private readonly List<string> songPaths = new List<string> { "Music/Citadel_March", "Music/Forgotten_Halls", "Music/Arcane_Pursuit", "Music/Crystal_Sanctum", "Music/Final_Guardian" };
    private List<int> playlist = new List<int>();
    private int playlistPosition;
    private readonly Dictionary<int, Material> materialCache = new Dictionary<int, Material>();
    private readonly Dictionary<Material, Color> materialBaseColors = new Dictionary<Material, Color>();
    private readonly Dictionary<string, float> statusTimers = new Dictionary<string, float>();
    private Vector3 checkpointPosition = new Vector3(0f, 0.5f, -2.3f);
    private bool sigilsRevealed;
    private bool returnOptionsToPause;
    private bool wasPausedBeforeOptions;
    private float fpsTimer;
    private int frameCount;
    private int fpsValue;
    private float loadingTimer;
    private bool pendingNextLevel;
    private bool transitionPrepared;
    private GUIStyle titleStyle, subtitleStyle, bodyStyle, buttonStyle, tinyStyle, headingStyle;
    private bool stylesReady;
    private Vector2 optionsScroll;

    private static readonly string[] LevelNames = {
        "The Timed Nightmare", "The Ember Vault", "The Glass Labyrinth", "The Broken Causeway",
        "The Stone Spire", "The Drowned Archive", "The Clockwork Choir", "The Mirror Bridge",
        "The Hollow Crown", "The Shadow Citadel"
    };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Application.targetFrameRate = 144;
        QualitySettings.vSyncCount = 0;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        LoadSettings();
        CreateEnvironment();
        CreateMenuCastle();
        SetupMusic();
        ApplySettings();
        SetCursor(false);
    }

    private void Update()
    {
        fpsTimer += Time.unscaledDeltaTime;
        frameCount++;
        if (fpsTimer >= 0.5f)
        {
            fpsValue = Mathf.RoundToInt(frameCount / fpsTimer);
            fpsTimer = 0f;
            frameCount = 0;
        }

        if (Settings.musicEnabled && musicSource != null && !musicSource.isPlaying && (Mode == ScreenMode.Playing || Mode == ScreenMode.MainMenu || Mode == ScreenMode.Paused))
            PlayNextTrack();

        // Use Unity's built-in legacy Input Manager to avoid requiring the Input System package.
        if (Input.GetKeyDown(KeyCode.F3))
        {
            Settings.showFps = !Settings.showFps;
            SaveSettings();
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (Mode == ScreenMode.Playing) PauseGame();
            else if (Mode == ScreenMode.Paused) ResumeGame();
            else if (Mode == ScreenMode.Options) CloseOptions();
        }

        if (Mode == ScreenMode.Playing && Player != null)
        {
            Mana = Mathf.Min(100f, Mana + Time.deltaTime * 4f);
            if (Player.transform.position.y < -12f)
            {
                Player.Teleport(checkpointPosition);
                ResetPuzzleStateAfterFall();
                SetStatus("You returned to your last anchor. Unsolved trials have reset.", 2.8f);
            }
            CheckSigilPickup();
            CheckStoneStepLanding();
            TickPuzzleProgress(Time.deltaTime);
            UpdateGateVisual();
            if (Player.transform.position.z > 38.2f && Mathf.Abs(Player.transform.position.x) < 2f)
            {
                if (AllPuzzlesSolved())
                {
                    if (LevelNumber >= 100)
                    {
                        Mode = ScreenMode.Victory;
                        Player.ControlsEnabled = false;
                        SetCursor(false);
                    }
                    else BeginLevelTransition();
                }
                else if (string.IsNullOrEmpty(StatusMessage)) SetStatus("The gate is sealed. " + GetMissingPuzzleSummary(), 2.5f);
            }
            TickStatusTimers();
        }

        if (Mode == ScreenMode.Loading && transitionPrepared)
        {
            loadingTimer += Time.unscaledDeltaTime;
            // Wait for rendering to recover after preparing the level; after eight seconds use a safety fallback.
            if (loadingTimer >= 0.4f && (fpsValue > 100 || loadingTimer >= 8f)) CompleteLevelTransition();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void CreateEnvironment()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.46f, 0.54f);
        RenderSettings.ambientIntensity = 0.8f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.23f, 0.29f, 0.39f);
        RenderSettings.fogDensity = 0.006f;

        GameObject lightObject = new GameObject("Moonlit Directional Light");
        sun = lightObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(0.78f, 0.84f, 0.96f);
        sun.intensity = 1.05f;
        sun.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
        sun.shadows = LightShadows.Soft;

        GameObject cameraObject = new GameObject("Main Menu Camera");
        menuCamera = cameraObject.AddComponent<Camera>();
        menuAudioListener = cameraObject.AddComponent<AudioListener>();
        menuCamera.tag = "MainCamera";
        menuCamera.clearFlags = CameraClearFlags.SolidColor;
        menuCamera.backgroundColor = new Color(0.20f, 0.27f, 0.38f);
        menuCamera.fieldOfView = 55f;
        menuCamera.nearClipPlane = 0.03f;
        menuCamera.farClipPlane = 180f;
        cameraObject.transform.position = new Vector3(0f, 4.0f, -10f);
        cameraObject.transform.LookAt(new Vector3(0f, 2.0f, 2f));
    }

    private void CreateMenuCastle()
    {
        menuCastle = new GameObject("Menu Castle Preview");
        Material stone = MakeStoneMaterial(new Color(0.31f, 0.35f, 0.39f));
        Material trim = MakeStoneMaterial(new Color(0.47f, 0.49f, 0.49f));
        Material dark = MakeStoneMaterial(new Color(0.07f, 0.09f, 0.12f));
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject tower = MakePrimitive(PrimitiveType.Cylinder, "Round Castle Tower", new Vector3(side * 2.4f, 2.0f, 2.4f), new Vector3(1.7f, 2.1f, 1.7f), stone, false, menuCastle.transform);
            MakePrimitive(PrimitiveType.Cylinder, "Tower Crenellation Ring", new Vector3(side * 2.4f, 4.0f, 2.4f), new Vector3(1.9f, 0.18f, 1.9f), trim, false, menuCastle.transform);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 2f / 8f;
                Vector3 pos = new Vector3(side * 2.4f + Mathf.Cos(angle) * 0.78f, 4.22f, 2.4f + Mathf.Sin(angle) * 0.78f);
                MakePrimitive(PrimitiveType.Cube, "Battlement", pos, new Vector3(0.24f, 0.38f, 0.24f), trim, false, menuCastle.transform);
            }
            // Horizontal masonry bands give the preview a built architectural silhouette.
            for (int band = 0; band < 5; band++)
                MakePrimitive(PrimitiveType.Cylinder, "Tower Masonry Course", new Vector3(side * 2.4f, 0.55f + band * 0.68f, 2.4f), new Vector3(1.73f, 0.055f, 1.73f), trim, false, menuCastle.transform);
        }
        MakePrimitive(PrimitiveType.Cube, "Gatehouse Lintel", new Vector3(0f, 3.55f, 2.3f), new Vector3(3.8f, 0.62f, 1.25f), trim, false, menuCastle.transform);
        MakePrimitive(PrimitiveType.Cube, "Shadowed Gate", new Vector3(0f, 1.55f, 2.87f), new Vector3(2.2f, 2.7f, 0.12f), dark, false, menuCastle.transform);
        for (int i = 0; i < 3; i++)
            MakePrimitive(PrimitiveType.Cube, "Entrance Stair", new Vector3(0f, -0.05f + i * 0.20f, 5.0f - i * 1.0f), new Vector3(4.0f - i * 0.3f, 0.35f, 2.4f), stone, false, menuCastle.transform);
    }

    private void SetupMusic()
    {
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = false;
        musicSource.playOnAwake = false;
        musicSource.volume = Settings.musicVolume;
        BuildPlaylist();
        if (Settings.musicEnabled) PlayNextTrack();
    }

    private void BuildPlaylist()
    {
        // Shuffle five track groups; Forgotten Halls appears once, each other group twice consecutively.
        List<int> groups = new List<int> { 0, 1, 2, 3, 4 };
        for (int i = groups.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int temp = groups[i]; groups[i] = groups[j]; groups[j] = temp;
        }
        playlist.Clear();
        foreach (int group in groups)
        {
            int copies = group == 1 ? 1 : 2;
            for (int i = 0; i < copies; i++) playlist.Add(group);
        }
        playlistPosition = 0;
    }

    private void PlayNextTrack()
    {
        if (musicSource == null || !Settings.musicEnabled) return;
        if (playlistPosition >= playlist.Count) BuildPlaylist();
        int index = playlist[playlistPosition++];
        AudioClip clip = Resources.Load<AudioClip>(songPaths[index]);
        if (clip == null) return;
        musicSource.clip = clip;
        musicSource.volume = Settings.musicVolume;
        musicSource.Play();
    }

    public void StartGame()
    {
        LevelNumber = 1;
        SigilsCollected = 0;
        Mana = 100f;
        checkpointPosition = new Vector3(0f, 0.5f, -2.3f);
        menuCamera.enabled = false;
        if (menuAudioListener != null) menuAudioListener.enabled = false;
        if (menuCastle != null) menuCastle.SetActive(false);
        BuildLevel();
        GameObject playerObject = new GameObject("Player");
        playerObject.transform.position = checkpointPosition;
        CharacterController cc = playerObject.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.32f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        Player = playerObject.AddComponent<ShadowRuinsPlayer>();
        Player.Initialize(this, cc);
        Mode = ScreenMode.Playing;
        SetCursor(true);
        SetStatus(GetPuzzleIntro(), 5f);
    }

    public void ReturnToMainMenu()
    {
        if (Player != null)
        {
            // Disable the gameplay listener before enabling the menu listener to avoid duplicate-listener warnings.
            if (Player.ViewCamera != null)
            {
                AudioListener playerListener = Player.ViewCamera.GetComponent<AudioListener>();
                if (playerListener != null) playerListener.enabled = false;
            }
            Destroy(Player.gameObject);
        }
        Player = null;
        if (levelRoot != null) Destroy(levelRoot.gameObject);
        levelRoot = null;
        if (menuCamera != null) menuCamera.enabled = true;
        if (menuAudioListener != null) menuAudioListener.enabled = true;
        if (menuCastle != null) menuCastle.SetActive(true);
        Mode = ScreenMode.MainMenu;
        SetCursor(false);
    }

    public void PauseGame()
    {
        if (Mode != ScreenMode.Playing) return;
        Mode = ScreenMode.Paused;
        if (Player != null) Player.ControlsEnabled = false;
        SetCursor(false);
    }

    public void ResumeGame()
    {
        if (Mode != ScreenMode.Paused) return;
        Mode = ScreenMode.Playing;
        if (Player != null) Player.ControlsEnabled = true;
        SetCursor(true);
    }

    private void SetCursor(bool captured)
    {
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    private void BeginLevelTransition()
    {
        Mode = ScreenMode.Loading;
        pendingNextLevel = true;
        transitionPrepared = false;
        loadingTimer = 0f;
        SetCursor(false);
        if (Player != null) Player.ControlsEnabled = false;
        StatusMessage = "Preparing the next chamber... waiting for FPS above 100.";
        StartCoroutine(PrepareNextLevelAfterLoadingFrame());
    }

    private IEnumerator PrepareNextLevelAfterLoadingFrame()
    {
        // Let Unity render the loading overlay once before generating the next level's geometry.
        yield return new WaitForEndOfFrame();
        if (!pendingNextLevel) yield break;
        LevelNumber = Mathf.Min(100, LevelNumber + 1);
        SigilsCollected = 0;
        Mana = 100f;
        checkpointPosition = new Vector3(0f, 0.5f, -2.3f);
        BuildLevel();
        if (Player != null) Player.Teleport(checkpointPosition);
        // Reset the FPS sampling window so the gate measures the freshly generated chamber,
        // not a stale high value from the previous level.
        fpsTimer = 0f;
        frameCount = 0;
        fpsValue = 0;
        loadingTimer = 0f;
        transitionPrepared = true;
    }

    private void CompleteLevelTransition()
    {
        if (!pendingNextLevel || !transitionPrepared) return;
        pendingNextLevel = false;
        transitionPrepared = false;
        if (Player != null) Player.ControlsEnabled = true;
        Mode = ScreenMode.Playing;
        SetCursor(true);
        SetStatus("Level " + LevelNumber + "/100 — " + CurrentLevelName() + ". " + GetPuzzleIntro(), 5f);
    }

    private void BuildLevel()
    {
        if (levelRoot != null) Destroy(levelRoot.gameObject);
        levelRoot = new GameObject("Level " + LevelNumber + " Geometry").transform;
        sigils.Clear();
        sigilPlatforms.Clear();
        sigilLabels.Clear();
        sigilGot.Clear();
        revealTimer = 0f;
        revealCooldown = 0f;
        glyphTrials.Clear();
        bridgeTrials.Clear();
        currentGlyphSequence.Clear();
        sigilsRevealed = false;
        echoPuzzleSolved = false;
        echoAnchorActive = false;
        echoTimer = 0f;
        pastSeal = null;
        presentSeal = null;
        echoVisual = null;
        highCrystal = null;
        chronoWarning = null;
        portalMarker = null;
        portalRenderer = null;
        chronoPlatform = null;
        activeStoneStep = null;
        highCrystalCollected = false;
        chronoPuzzleSolved = false;
        chronoStarted = false;
        chronoCollapseTimer = 0f;
        chronoBoundTimer = 0f;
        glyphSequenceIndex = 0;
        stoneStepLanded = false;
        crystalJumpArmed = false;
        crystalSpin = 0f;

        // The original ten-chamber puzzle pattern repeats through the 100-chamber campaign.
        puzzleProfileIndex = (LevelNumber - 1) % 10;
        requiresCrystal = puzzleProfileIndex == 0 || puzzleProfileIndex == 4 || puzzleProfileIndex == 8 || puzzleProfileIndex == 9;
        requiresChrono = puzzleProfileIndex == 4 || puzzleProfileIndex == 6 || puzzleProfileIndex == 8 || puzzleProfileIndex == 9;
        SigilsRequired = (puzzleProfileIndex == 0 || puzzleProfileIndex == 7) ? 3 :
                         (puzzleProfileIndex == 2 || puzzleProfileIndex == 5 || puzzleProfileIndex == 9) ? 4 : 0;
        SigilsCollected = 0;
        int bridgeCount = GetBridgeCountForProfile(puzzleProfileIndex);
        int[] bridgeGaps = bridgeCount == 2 ? new int[] { 2, 4 } : bridgeCount == 1 ? new int[] { 2 } : new int[0];

        Material stone = MakeStoneMaterial(LevelStoneColor());
        Material trim = MakeStoneMaterial(Color.Lerp(LevelStoneColor(), new Color(0.54f, 0.56f, 0.56f), 0.34f));
        Material dark = MakeStoneMaterial(new Color(0.055f, 0.07f, 0.10f));
        Material runeMat = MakeEmissiveMaterial(new Color(0.20f, 0.88f, 1f), new Color(0.05f, 0.6f, 1f) * 2.1f, 0.82f);
        Material glyphMat = MakeEmissiveMaterial(new Color(0.68f, 0.39f, 1f), new Color(0.37f, 0.08f, 0.95f) * 1.8f, 0.72f);
        Material plateMat = MakeEmissiveMaterial(new Color(0.24f, 0.78f, 0.88f), new Color(0.03f, 0.36f, 0.50f) * 1.6f, 0.78f);
        Material bridgeMat = MakeStoneMaterial(Color.Lerp(LevelStoneColor(), new Color(0.30f, 0.48f, 0.53f), 0.45f));
        Material crystalMat = MakeEmissiveMaterial(new Color(0.60f, 0.95f, 1f), new Color(0.15f, 0.82f, 1f) * 2.4f, 0.22f);
        Material chronoMat = MakeEmissiveMaterial(new Color(0.50f, 0.24f, 0.18f), new Color(0.85f, 0.16f, 0.04f) * 0.7f, 0.34f);

        MakePrimitive(PrimitiveType.Cube, "Starting Platform", new Vector3(0f, 0f, 0f), new Vector3(8f, 1f, 8f), stone, true, levelRoot);
        Vector3[] route = new Vector3[6];
        float[] tops = new float[6];
        bool[] skipped = new bool[6];
        int chronoLedgeIndex = puzzleProfileIndex == 9 ? 5 : 4;
        for (int i = 0; i < bridgeGaps.Length; i++)
            if (bridgeGaps[i] >= 0 && bridgeGaps[i] < skipped.Length) skipped[bridgeGaps[i]] = true;

        for (int i = 0; i < route.Length; i++)
        {
            float z = 7.2f + i * 5.05f;
            float x = Mathf.Sin((i + LevelNumber) * 0.83f) * 1.25f;
            float y = 0.20f + ((i + LevelNumber) % 3) * 0.30f;
            route[i] = new Vector3(x, y, z);
            tops[i] = y + 0.36f;
            if (skipped[i]) continue;

            bool isChrono = requiresChrono && i == chronoLedgeIndex;
            Material platformMat = isChrono ? chronoMat : stone;
            GameObject platform = MakePrimitive(PrimitiveType.Cube, isChrono ? "Crumbling Chrono Ledge" : "Jump Platform " + (i + 1), route[i], new Vector3(4.4f, 0.72f, 3.9f), platformMat, true, levelRoot);
            MakePrimitive(PrimitiveType.Cube, "Platform Edge Trim", route[i] + new Vector3(0f, -0.31f, 0f), new Vector3(4.55f, 0.12f, 4.0f), trim, false, levelRoot);
            if (isChrono)
            {
                chronoPlatform = platform;
                chronoWarning = MakePrimitive(PrimitiveType.Sphere, "Chrono Crack Warning", route[i] + new Vector3(0.85f, 0.72f, 0f), new Vector3(0.23f, 0.23f, 0.23f), chronoMat, false, levelRoot);
                chronoWarningRenderer = chronoWarning.GetComponent<Renderer>();
            }
            if ((i + LevelNumber) % 2 == 0)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    MakePrimitive(PrimitiveType.Cylinder, "Ruined Pillar", route[i] + new Vector3(side * 2.65f, 0.65f, 0f), new Vector3(0.36f, 0.80f, 0.36f), trim, false, levelRoot);
                    MakePrimitive(PrimitiveType.Cube, "Pillar Capital", route[i] + new Vector3(side * 2.65f, 1.48f, 0f), new Vector3(0.64f, 0.16f, 0.64f), trim, false, levelRoot);
                }
            }
        }

        // The bridge socket sits on the source bank; the crossing platform is solid only after E activates it.
        foreach (int gapIndex in bridgeGaps)
        {
            int sourceIndex = gapIndex - 1;
            int destinationIndex = gapIndex + 1;
            if (sourceIndex < 0 || destinationIndex >= route.Length) continue;
            Vector3 source = route[sourceIndex];
            Vector3 destination = route[destinationIndex];
            Vector3 bridgePosition = new Vector3((source.x + destination.x) * 0.5f,
                (tops[sourceIndex] + tops[destinationIndex]) * 0.5f - 0.22f,
                (source.z + destination.z) * 0.5f);
            GameObject bridge = MakePrimitive(PrimitiveType.Cube, "Sealed Arcane Bridge", bridgePosition,
                new Vector3(3.8f, 0.44f, 7.5f), bridgeMat, true, levelRoot);
            bridge.SetActive(false);
            Vector3 socketPosition = new Vector3(source.x, tops[sourceIndex] + 0.95f, source.z + 1.25f);
            GameObject socket = MakePrimitive(PrimitiveType.Sphere, "Bridge Rune Socket", socketPosition,
                new Vector3(0.52f, 0.52f, 0.52f), runeMat, false, levelRoot);
            MakeWorldLabel("BRIDGE", socketPosition + Vector3.up * 0.58f, new Color(0.70f, 0.98f, 1f));
            bridgeTrials.Add(new BridgeTrial { socket = socket, bridge = bridge, sourceIndex = sourceIndex,
                gapIndex = gapIndex, destinationIndex = destinationIndex, active = false, entered = false, completed = false });
        }

        MakePrimitive(PrimitiveType.Cube, "Gate Approach", new Vector3(0f, 0.0f, 36.8f), new Vector3(8.4f, 1f, 7.5f), stone, true, levelRoot);
        BuildGate(stone, trim, dark, runeMat);

        // Numbered sigils sit on spectral side ledges. Q reveals each ledge only briefly.
        int[] sigilCandidates = { 0, 1, 3, 5, 4, 2 };
        int sigilPlaced = 0;
        for (int c = 0; c < sigilCandidates.Length && sigilPlaced < SigilsRequired; c++)
        {
            int platformIndex = sigilCandidates[c];
            if (platformIndex < 0 || platformIndex >= route.Length || skipped[platformIndex]) continue;
            Vector3 p = route[platformIndex];
            float side = sigilPlaced % 2 == 0 ? 1f : -1f;
            Vector3 ledgePosition = new Vector3(p.x + side * 3.30f, tops[platformIndex] + 0.25f, p.z);
            GameObject spectralLedge = MakePrimitive(PrimitiveType.Cube, "Spectral Sigil Ledge " + (sigilPlaced + 1),
                ledgePosition, new Vector3(2.35f, 0.42f, 2.35f),
                MakeStoneMaterial(Color.Lerp(LevelStoneColor(), new Color(0.28f, 0.47f, 0.56f), 0.48f)), true, levelRoot);
            spectralLedge.SetActive(false);
            sigilPlatforms.Add(spectralLedge);

            Vector3 runePosition = new Vector3(ledgePosition.x, ledgePosition.y + 0.78f, ledgePosition.z);
            GameObject rune = MakePrimitive(PrimitiveType.Cube, "Hidden Sigil " + (sigilPlaced + 1), runePosition,
                new Vector3(0.46f, 0.58f, 0.22f), runeMat, false, levelRoot);
            rune.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            rune.SetActive(false);
            sigils.Add(rune);
            sigilGot.Add(false);

            GameObject sigilLabel = MakeWorldLabel("SIGIL " + (sigilPlaced + 1), runePosition + Vector3.up * 0.58f,
                new Color(0.72f, 0.97f, 1f));
            sigilLabel.SetActive(false);
            sigilLabels.Add(sigilLabel);
            sigilPlaced++;
        }
        SigilsRequired = sigilPlaced;

        // Ordered glyph trials: stand on the marked ledge and press E in the displayed order.
        string[] sequence = GetGlyphSequenceForProfile(puzzleProfileIndex);
        currentGlyphSequence.AddRange(sequence);
        int[] glyphCandidates = { 0, 1, 3, 5, 2, 4 };
        int glyphPlaced = 0;
        for (int candidate = 0; candidate < glyphCandidates.Length && glyphPlaced < sequence.Length; candidate++)
        {
            int platformIndex = glyphCandidates[candidate];
            if (skipped[platformIndex]) continue;
            Vector3 p = route[platformIndex];
            float side = glyphPlaced % 2 == 0 ? 0.82f : -0.82f;
            Vector3 glyphPosition = new Vector3(p.x + side, tops[platformIndex] + 1.18f, p.z);
            GameObject marker = MakePrimitive(PrimitiveType.Cube, "Sequence Glyph " + sequence[glyphPlaced], glyphPosition,
                new Vector3(0.72f, 0.82f, 0.72f), glyphMat, false, levelRoot);
            marker.transform.rotation = Quaternion.Euler(0f, 45f, 45f);
            MakeWorldLabel(sequence[glyphPlaced], glyphPosition + Vector3.up * 0.62f, new Color(0.88f, 0.76f, 1f));
            glyphTrials.Add(new GlyphTrial { marker = marker, renderer = marker.GetComponent<Renderer>(), symbol = sequence[glyphPlaced],
                sequenceIndex = glyphPlaced, solved = false, platformIndex = platformIndex });
            glyphPlaced++;
        }

        // A memory echo must be anchored at PAST before the player reaches PRESENT.
        Vector3 pastRoute = route[0];
        Vector3 presentRoute = route[3];
        pastSealPosition = new Vector3(pastRoute.x - 1.05f, tops[0] + 0.08f, pastRoute.z);
        presentSealPosition = new Vector3(presentRoute.x + 1.05f, tops[3] + 0.08f, presentRoute.z);
        pastSeal = MakePrimitive(PrimitiveType.Cylinder, "PAST Memory Seal", pastSealPosition,
            new Vector3(1.30f, 0.06f, 1.30f), plateMat, false, levelRoot);
        presentSeal = MakePrimitive(PrimitiveType.Cylinder, "PRESENT Memory Seal", presentSealPosition,
            new Vector3(1.30f, 0.06f, 1.30f), plateMat, false, levelRoot);
        pastSealRenderer = pastSeal.GetComponent<Renderer>();
        presentSealRenderer = presentSeal.GetComponent<Renderer>();
        MakeWorldLabel("PAST · Z", pastSealPosition + Vector3.up * 0.8f, new Color(0.64f, 0.94f, 1f));
        MakeWorldLabel("PRESENT", presentSealPosition + Vector3.up * 0.8f, new Color(0.64f, 0.94f, 1f));
        echoVisual = MakePrimitive(PrimitiveType.Sphere, "Echo Memory Projection", pastSealPosition + Vector3.up * 0.9f,
            new Vector3(0.55f, 0.78f, 0.55f), runeMat, false, levelRoot);
        echoVisual.SetActive(false);

        if (requiresCrystal)
        {
            Vector3 p = route[3];
            Vector3 crystalPosition = new Vector3(p.x, tops[3] + 2.30f, p.z);
            highCrystal = MakePrimitive(PrimitiveType.Cube, "High Crystal", crystalPosition,
                new Vector3(0.72f, 0.92f, 0.72f), crystalMat, false, levelRoot);
            highCrystal.transform.rotation = Quaternion.Euler(45f, 45f, 45f);
            MakeWorldLabel("HIGH CRYSTAL · R + JUMP", crystalPosition + Vector3.up * 0.75f, new Color(0.70f, 0.98f, 1f));
            highCrystalRenderer = highCrystal.GetComponent<Renderer>();
        }

        // Broken walls frame the route but remain outside the intended jumping line.
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < 4; i++)
            {
                float z = 4f + i * 8f;
                MakePrimitive(PrimitiveType.Cube, "Broken Wall Base", new Vector3(side * 5.2f, 0.2f, z), new Vector3(0.85f, 0.45f, 3.2f), stone, true, levelRoot);
                if ((i + LevelNumber) % 2 == 0)
                    MakePrimitive(PrimitiveType.Cube, "Broken Wall Upper", new Vector3(side * 5.2f, 1.1f, z + 0.5f), new Vector3(0.75f, 1.35f, 1.1f), trim, true, levelRoot);
            }
        }
        if (Player != null) Player.ApplyRealism(Settings.realism);
    }

    private Material MakeEmissiveMaterial(Color color, Color emission, float smoothness)
    {
        Material material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Metallic", 0.03f);
        return material;
    }

    private GameObject MakeWorldLabel(string label, Vector3 position, Color color)
    {
        GameObject labelObject = new GameObject("Rune Label - " + label);
        labelObject.transform.SetParent(levelRoot, true);
        labelObject.transform.position = position;
        labelObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        TextMesh text = labelObject.AddComponent<TextMesh>();
        text.text = label;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 48;
        text.characterSize = 0.065f;
        text.color = color;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        MeshRenderer textRenderer = labelObject.GetComponent<MeshRenderer>();
        if (textRenderer != null) textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return labelObject;
    }

    private int GetBridgeCountForProfile(int profile)
    {
        if (profile == 0 || profile == 1 || profile == 5 || profile == 8) return 1;
        if (profile == 3 || profile == 7 || profile == 9) return 2;
        return 0;
    }

    private string[] GetGlyphSequenceForProfile(int profile)
    {
        switch (profile)
        {
            case 0: return new[] { "MOON", "CROWN", "EYE" };
            case 1: return new[] { "MOON", "CROWN", "EYE", "SEVER" };
            case 4: return new[] { "ASH", "FLAME", "CINDER" };
            case 6: return new[] { "BELL I", "BELL II", "BELL III", "BELL IV" };
            case 8: return new[] { "CROWN", "SHADE", "THORN", "HEART" };
            case 9: return new[] { "MOON", "CROWN", "EYE", "HEART" };
            default: return new string[0];
        }
    }

    private string GetPuzzleIntro()
    {
        List<string> parts = new List<string>();
        if (SigilsRequired > 0) parts.Add("Q reveals " + SigilsRequired + " numbered sigils briefly; collect them in order.");
        if (bridgeTrials.Count > 0) parts.Add("E at glowing bridge sockets, then cross the bridge(s).");
        if (glyphTrials.Count > 0) parts.Add("Glyph order: " + string.Join(" → ", currentGlyphSequence.ToArray()) + ".");
        parts.Add("Z on PAST, then reach PRESENT before the echo fades.");
        if (requiresCrystal) parts.Add("R: land on Stone Step, jump again to the high crystal.");
        if (requiresChrono) parts.Add("T on the orange ledge before it collapses.");
        string text = string.Join(" ", parts.ToArray());
        if (text.Length > 220) text = text.Substring(0, 217) + "...";
        return text;
    }

    private string GetMissingPuzzleSummary()
    {
        List<string> missing = new List<string>();
        if (SigilsCollected < SigilsRequired) missing.Add("sigils " + SigilsCollected + "/" + SigilsRequired);
        for (int i = 0; i < bridgeTrials.Count; i++)
            if (!bridgeTrials[i].completed) missing.Add("bridge " + (i + 1));
        if (glyphSequenceIndex < glyphTrials.Count) missing.Add("glyph sequence " + glyphSequenceIndex + "/" + glyphTrials.Count);
        if (!echoPuzzleSolved) missing.Add("PAST → PRESENT echo");
        if (requiresCrystal && !highCrystalCollected) missing.Add("high crystal");
        if (requiresChrono && !chronoPuzzleSolved) missing.Add("Chrono Bind on the orange ledge");
        return missing.Count == 0 ? "Complete the remaining chamber trials." : "Still needed: " + string.Join(", ", missing.ToArray()) + ".";
    }

    private bool AllPuzzlesSolved()
    {
        if (SigilsCollected < SigilsRequired) return false;
        for (int i = 0; i < bridgeTrials.Count; i++) if (!bridgeTrials[i].completed) return false;
        for (int i = 0; i < glyphTrials.Count; i++) if (!glyphTrials[i].solved) return false;
        if (!echoPuzzleSolved) return false;
        if (requiresCrystal && !highCrystalCollected) return false;
        if (requiresChrono && !chronoPuzzleSolved) return false;
        return true;
    }

    private void TickRevealProgress(float dt)
    {
        revealCooldown = Mathf.Max(0f, revealCooldown - dt);
        if (!sigilsRevealed) return;
        revealTimer = Mathf.Max(0f, revealTimer - dt);
        if (SigilsCollected >= SigilsRequired || revealTimer <= 0f)
        {
            HideSpectralRoute();
            if (SigilsCollected < SigilsRequired)
                SetStatus("The spectral route faded. Press Q again to reveal the remaining sigils.", 2.4f);
        }
    }

    private void HideSpectralRoute()
    {
        sigilsRevealed = false;
        revealTimer = 0f;
        for (int i = 0; i < sigilPlatforms.Count; i++)
            if (sigilPlatforms[i] != null) sigilPlatforms[i].SetActive(false);
        for (int i = 0; i < sigils.Count; i++)
        {
            if (sigils[i] != null && (i >= sigilGot.Count || !sigilGot[i])) sigils[i].SetActive(false);
            if (i < sigilLabels.Count && sigilLabels[i] != null) sigilLabels[i].SetActive(false);
        }
    }

    private void TickPuzzleProgress(float dt)
    {
        if (Player == null || levelRoot == null) return;
        TickRevealProgress(dt);
        if (echoAnchorActive && !echoPuzzleSolved)
        {
            echoTimer -= dt;
            if (echoTimer <= 0f)
            {
                echoTimer = 0f;
                echoAnchorActive = false;
                if (echoVisual != null) echoVisual.SetActive(false);
                UpdateSealVisuals();
                SetStatus("The echo faded. Return to PAST and press Z again.", 2.4f);
            }
        }

        for (int i = 0; i < bridgeTrials.Count; i++)
        {
            BridgeTrial trial = bridgeTrials[i];
            if (!trial.active || trial.completed) continue;
            Vector3 playerPosition = Player.transform.position;
            Vector3 bridgePosition = trial.bridge.transform.position;
            if (!trial.entered && Mathf.Abs(playerPosition.z - bridgePosition.z) < 3.0f && Mathf.Abs(playerPosition.x - bridgePosition.x) < 2.2f)
                trial.entered = true;
            Vector3 destination = new Vector3(trial.bridge.transform.position.x, Player.transform.position.y, 7.2f + trial.destinationIndex * 5.05f);
            if (trial.entered && playerPosition.z >= destination.z - 1.35f && Mathf.Abs(playerPosition.x - destination.x) < 2.7f)
            {
                trial.completed = true;
                SetSocketComplete(trial.socket);
                SetStatus("Bridge " + (i + 1) + " crossed and sealed. The route remembers your passage.", 2.2f);
            }
        }

        if (echoAnchorActive && !echoPuzzleSolved && IsPlayerAtSeal(presentSealPosition))
        {
            echoPuzzleSolved = true;
            echoAnchorActive = false;
            echoTimer = 0f;
            if (echoVisual != null) echoVisual.SetActive(false);
            UpdateSealVisuals();
            SetStatus("PAST AND PRESENT LINKED. Memory trial complete!", 2.8f);
        }

        if (requiresCrystal && !highCrystalCollected && highCrystal != null)
        {
            highCrystal.transform.Rotate(0f, 70f * dt, 30f * dt, Space.World);
            crystalSpin += dt;
            if (crystalJumpArmed && Vector2.Distance(new Vector2(Player.transform.position.x, Player.transform.position.z),
                new Vector2(highCrystal.transform.position.x, highCrystal.transform.position.z)) < 1.25f &&
                Player.transform.position.y >= highCrystal.transform.position.y - 1.5f)
            {
                highCrystalCollected = true;
                highCrystal.SetActive(false);
                crystalJumpArmed = false;
                SetStatus("HIGH CRYSTAL CLAIMED. Stone Step trial complete!", 2.8f);
            }
        }

        if (requiresChrono && !chronoPuzzleSolved && chronoPlatform != null)
        {
            if (chronoStarted)
            {
                chronoCollapseTimer -= dt;
                if (chronoWarningRenderer != null && IsPlayerOnPlatform(chronoPlatform))
                {
                    float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 9f);
                    chronoWarningRenderer.material.color = Color.Lerp(new Color(0.95f, 0.24f, 0.07f), Color.yellow, pulse);
                }
                if (chronoWarning != null) chronoWarning.transform.Rotate(0f, 100f * dt, 0f, Space.World);
                if (chronoCollapseTimer <= 0f)
                {
                    Collider col = chronoPlatform.GetComponent<Collider>();
                    if (col != null) col.enabled = false;
                    Renderer rend = chronoPlatform.GetComponent<Renderer>();
                    if (rend != null) rend.enabled = false;
                    chronoStarted = false;
                    SetStatus("The ledge collapsed. Fall back to an anchor and try again.", 2.5f);
                }
            }
            else if (IsPlayerOnPlatform(chronoPlatform))
            {
                chronoStarted = true;
                chronoCollapseTimer = 3.1f;
                SetStatus("The orange ledge is crumbling! Press T before the 3-second collapse.", 2.4f);
            }
        }
    }

    private bool IsPlayerAtSeal(Vector3 sealPosition)
    {
        if (Player == null) return false;
        Vector3 p = Player.transform.position;
        return Mathf.Abs(p.y - sealPosition.y) < 1.0f &&
               Vector2.Distance(new Vector2(p.x, p.z), new Vector2(sealPosition.x, sealPosition.z)) < 0.95f;
    }

    private bool IsPlayerOnPlatform(GameObject platform)
    {
        if (Player == null || platform == null || !platform.activeInHierarchy) return false;
        Vector3 pos = Player.transform.position;
        Vector3 center = platform.transform.position;
        Vector3 scale = platform.transform.localScale;
        float top = center.y + scale.y * 0.5f;
        return Mathf.Abs(pos.y - top) < 0.68f && Mathf.Abs(pos.x - center.x) < scale.x * 0.5f - 0.15f &&
               Mathf.Abs(pos.z - center.z) < scale.z * 0.5f - 0.15f;
    }

    private void UpdateSealVisuals()
    {
        if (pastSealRenderer != null)
        {
            pastSealRenderer.material.color = echoPuzzleSolved ? new Color(0.28f, 1f, 0.72f) : echoAnchorActive ? new Color(0.36f, 0.95f, 1f) : new Color(0.24f, 0.78f, 0.88f);
            pastSealRenderer.material.SetColor("_EmissionColor", echoPuzzleSolved ? new Color(0.08f, 0.75f, 0.34f) : echoAnchorActive ? new Color(0.05f, 0.72f, 1f) : new Color(0.03f, 0.36f, 0.50f));
        }
        if (presentSealRenderer != null)
        {
            presentSealRenderer.material.color = echoPuzzleSolved ? new Color(0.28f, 1f, 0.72f) : new Color(0.24f, 0.78f, 0.88f);
            presentSealRenderer.material.SetColor("_EmissionColor", echoPuzzleSolved ? new Color(0.08f, 0.75f, 0.34f) : new Color(0.03f, 0.36f, 0.50f));
        }
    }

    private void SetSocketComplete(GameObject socket)
    {
        if (socket == null) return;
        Renderer renderer = socket.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = new Color(0.38f, 1f, 0.72f);
            renderer.material.SetColor("_EmissionColor", new Color(0.04f, 0.8f, 0.28f) * 2f);
        }
    }

    private void ResetPuzzleStateAfterFall()
    {
        HideSpectralRoute();
        if (echoPuzzleSolved == false)
        {
            echoAnchorActive = false;
            echoTimer = 0f;
            if (echoVisual != null) echoVisual.SetActive(false);
            UpdateSealVisuals();
        }
        for (int i = 0; i < bridgeTrials.Count; i++)
        {
            BridgeTrial trial = bridgeTrials[i];
            if (trial.completed) continue;
            trial.active = false;
            trial.entered = false;
            if (trial.bridge != null) trial.bridge.SetActive(false);
            if (trial.socket != null)
            {
                Renderer r = trial.socket.GetComponent<Renderer>();
                if (r != null) r.material.color = new Color(0.20f, 0.88f, 1f);
            }
        }
        if (chronoPlatform != null)
        {
            Collider col = chronoPlatform.GetComponent<Collider>();
            Renderer rend = chronoPlatform.GetComponent<Renderer>();
            if (col != null) col.enabled = true;
            if (rend != null) rend.enabled = true;
            chronoStarted = false;
            chronoCollapseTimer = 0f;
            chronoBoundTimer = 0f;
        }
        if (activeStoneStep != null) Destroy(activeStoneStep);
        activeStoneStep = null;
        stoneStepLanded = false;
        crystalJumpArmed = false;
    }

    private void BuildGate(Material stone, Material trim, Material dark, Material runeMat)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 towerPosition = new Vector3(side * 2.65f, 2.25f, 40.25f);
            MakePrimitive(PrimitiveType.Cylinder, "Gatehouse Round Tower", towerPosition, new Vector3(1.18f, 2.3f, 1.18f), stone, true, levelRoot);
            MakePrimitive(PrimitiveType.Cylinder, "Tower Battlement Ring", towerPosition + Vector3.up * 2.22f, new Vector3(1.30f, 0.15f, 1.30f), trim, false, levelRoot);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                Vector3 crenel = towerPosition + Vector3.up * 2.48f + new Vector3(Mathf.Cos(a) * 0.48f, 0f, Mathf.Sin(a) * 0.48f);
                MakePrimitive(PrimitiveType.Cube, "Tower Crenel", crenel, new Vector3(0.24f, 0.34f, 0.24f), trim, false, levelRoot);
            }
            for (int band = 0; band < 5; band++)
                MakePrimitive(PrimitiveType.Cylinder, "Tower Masonry Course", towerPosition + Vector3.down * 1.65f + Vector3.up * band * 0.75f, new Vector3(1.20f, 0.035f, 1.20f), trim, false, levelRoot);
            MakePrimitive(PrimitiveType.Cube, "Gate Buttress", new Vector3(side * 1.6f, 1.4f, 40.0f), new Vector3(0.42f, 2.6f, 0.9f), trim, true, levelRoot);
        }
        MakePrimitive(PrimitiveType.Cube, "Gate Lintel", new Vector3(0f, 4.0f, 40.15f), new Vector3(4.2f, 0.65f, 1.25f), trim, true, levelRoot);
        MakePrimitive(PrimitiveType.Cube, "Gate Shadow", new Vector3(0f, 1.75f, 40.73f), new Vector3(2.7f, 2.9f, 0.16f), dark, false, levelRoot);
        for (int x = -2; x <= 2; x++)
        {
            MakePrimitive(PrimitiveType.Cube, "Gate Iron Bar", new Vector3(x * 0.46f, 1.7f, 40.62f), new Vector3(0.06f, 2.7f, 0.08f), trim, false, levelRoot);
        }
        for (int y = 0; y < 5; y++)
        {
            MakePrimitive(PrimitiveType.Cube, "Gate Stone Course", new Vector3(0f, 0.7f + y * 0.66f, 40.0f), new Vector3(3.4f, 0.05f, 1.34f), trim, false, levelRoot);
        }
        portalMarker = MakePrimitive(PrimitiveType.Cube, "Portal Marker", new Vector3(0f, 1.7f, 40.9f), new Vector3(1.7f, 2.4f, 0.06f), runeMat, false, levelRoot);
        portalRenderer = portalMarker.GetComponent<Renderer>();
        if (portalRenderer != null) portalRenderer.enabled = true;
    }

    private Color LevelStoneColor()
    {
        Color[] palette = {
            new Color(0.34f, 0.39f, 0.43f), new Color(0.40f, 0.32f, 0.29f),
            new Color(0.26f, 0.37f, 0.43f), new Color(0.35f, 0.39f, 0.39f),
            new Color(0.35f, 0.32f, 0.42f), new Color(0.27f, 0.39f, 0.38f),
            new Color(0.41f, 0.31f, 0.36f), new Color(0.26f, 0.37f, 0.41f),
            new Color(0.35f, 0.31f, 0.40f), new Color(0.31f, 0.31f, 0.40f)
        };
        Color c = palette[(LevelNumber - 1) % palette.Length];
        if (Settings.realism > 0) c = Color.Lerp(c, new Color(0.39f, 0.39f, 0.37f), Settings.realism == 2 ? 0.42f : 0.22f);
        return c;
    }

    private void UpdateGateVisual()
    {
        if (portalRenderer == null) return;
        bool open = AllPuzzlesSolved();
        portalRenderer.material.color = open ? new Color(0.34f, 1f, 0.72f) : new Color(0.20f, 0.88f, 1f);
        portalRenderer.material.SetColor("_EmissionColor", open ? new Color(0.05f, 0.95f, 0.35f) * 2.2f : new Color(0.05f, 0.6f, 1f) * 2.1f);
    }

    private void CheckSigilPickup()
    {
        if (!sigilsRevealed || Player == null) return;
        for (int i = 0; i < sigils.Count; i++)
        {
            GameObject rune = sigils[i];
            if (rune == null || i >= sigilGot.Count || sigilGot[i] || !rune.activeInHierarchy) continue;
            Vector3 playerPosition = Player.transform.position;
            Vector3 target = rune.transform.position;
            if (Mathf.Abs(playerPosition.y - target.y) >= 2.3f || Vector2.Distance(
                new Vector2(playerPosition.x, playerPosition.z), new Vector2(target.x, target.z)) >= 1.45f) continue;

            if (i == SigilsCollected)
            {
                sigilGot[i] = true;
                SigilsCollected++;
                rune.SetActive(false);
                if (i < sigilLabels.Count && sigilLabels[i] != null) sigilLabels[i].SetActive(false);
                Mana = Mathf.Min(100f, Mana + 16f);
                if (SigilsCollected >= SigilsRequired)
                {
                    HideSpectralRoute();
                    SetStatus("All numbered sigils collected. The sigil trial is complete.", 2.4f);
                }
                else SetStatus("SIGIL " + (i + 1) + " collected. Find SIGIL " + (SigilsCollected + 1) + " next.", 2.0f);
            }
            else
            {
                SigilsCollected = 0;
                for (int j = 0; j < sigilGot.Count; j++)
                {
                    sigilGot[j] = false;
                    if (sigils[j] != null) sigils[j].SetActive(sigilsRevealed);
                    if (j < sigilLabels.Count && sigilLabels[j] != null) sigilLabels[j].SetActive(sigilsRevealed);
                    if (j < sigilPlatforms.Count && sigilPlatforms[j] != null) sigilPlatforms[j].SetActive(sigilsRevealed);
                }
                SetStatus("Wrong order. Sigils reset. Start with SIGIL 1.", 2.4f);
            }
            break;
        }
    }

    public void PerformAction(string action)
    {
        if (Mode != ScreenMode.Playing || Player == null) return;
        switch (action)
        {
            case "Q":
                if (SigilsRequired <= 0)
                {
                    SetStatus("This chamber has no hidden sigils. Follow the marked puzzle trials.", 2.2f);
                }
                else if (SigilsCollected >= SigilsRequired)
                {
                    SetStatus("All numbered sigils are already collected.", 1.8f);
                }
                else if (sigilsRevealed)
                {
                    SetStatus("Spectral route visible: " + revealTimer.ToString("0.0") + "s. Next: SIGIL " + (SigilsCollected + 1) + ".", 1.8f);
                }
                else if (revealCooldown > 0f)
                {
                    SetStatus("The reveal is recharging: " + revealCooldown.ToString("0.0") + "s.", 1.5f);
                }
                else if (SpendMana(14f))
                {
                    sigilsRevealed = true;
                    revealTimer = 8.5f;
                    revealCooldown = 1.05f;
                    for (int i = 0; i < sigilPlatforms.Count; i++)
                        if (sigilPlatforms[i] != null) sigilPlatforms[i].SetActive(i < sigilGot.Count && !sigilGot[i]);
                    for (int i = 0; i < sigils.Count; i++)
                    {
                        if (sigils[i] != null) sigils[i].SetActive(i < sigilGot.Count && !sigilGot[i]);
                        if (i < sigilLabels.Count && sigilLabels[i] != null) sigilLabels[i].SetActive(i < sigilGot.Count && !sigilGot[i]);
                    }
                    SetStatus("Spectral route revealed for 8.5 seconds. Collect the sigils in number order.", 2.8f);
                }
                Player.PlaySpellGesture("Q");
                break;
            case "E":
                if (TryActivateBridge()) { Player.PlaySpellGesture("E"); break; }
                if (TryActivateGlyph()) { Player.PlaySpellGesture("E"); break; }
                if (!SpendMana(12f)) return;
                if (Settings.spellEffects) SpawnProjectile();
                SetStatus("Arcane bolt cast. Stand beside a bridge socket or glyph to use it on a puzzle.", 1.8f);
                Player.PlaySpellGesture("E");
                break;
            case "R":
                if (!SpendMana(18f)) return;
                if (Settings.spellEffects) SpawnStoneStep();
                SetStatus(requiresCrystal && !highCrystalCollected ? "Stone Step created. Land on it, then jump again toward the high crystal." : "Stone Step created temporarily.", 2.5f);
                Player.PlaySpellGesture("R");
                break;
            case "Z":
                TryAnchorEcho();
                Player.PlaySpellGesture("Z");
                break;
            case "T":
                TryChronoBind();
                Player.PlaySpellGesture("T");
                break;
        }
    }

    private bool TryActivateBridge()
    {
        if (bridgeTrials.Count == 0) return false;
        Vector3 playerPosition = Player.transform.position;
        for (int i = 0; i < bridgeTrials.Count; i++)
        {
            BridgeTrial trial = bridgeTrials[i];
            if (trial.completed || trial.socket == null) continue;
            Vector3 target = trial.socket.transform.position;
            bool closeEnough = Mathf.Abs(playerPosition.y - target.y) < 2.1f &&
                Vector2.Distance(new Vector2(playerPosition.x, playerPosition.z), new Vector2(target.x, target.z)) < 2.15f;
            if (!closeEnough || trial.active) continue;
            if (!SpendMana(10f)) return true;
            trial.active = true;
            if (trial.bridge != null) trial.bridge.SetActive(true);
            if (Settings.spellEffects) SpawnProjectile();
            SetStatus("Bridge formed! Cross it to the far bank to complete the trial.", 2.7f);
            return true;
        }
        return false;
    }

    private bool TryActivateGlyph()
    {
        if (glyphTrials.Count == 0) return false;
        Vector3 playerPosition = Player.transform.position;
        GlyphTrial candidate = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < glyphTrials.Count; i++)
        {
            GlyphTrial glyph = glyphTrials[i];
            if (glyph.solved || glyph.marker == null) continue;
            Vector3 target = glyph.marker.transform.position;
            float horizontal = Vector2.Distance(new Vector2(playerPosition.x, playerPosition.z), new Vector2(target.x, target.z));
            if (Mathf.Abs(playerPosition.y - target.y) > 2.0f || horizontal > 2.35f || horizontal >= bestDistance) continue;
            candidate = glyph;
            bestDistance = horizontal;
        }
        if (candidate == null) return false;
        if (!SpendMana(8f)) return true;
        if (candidate.sequenceIndex == glyphSequenceIndex)
        {
            candidate.solved = true;
            if (candidate.renderer != null)
            {
                candidate.renderer.material.color = new Color(0.38f, 1f, 0.70f);
                candidate.renderer.material.SetColor("_EmissionColor", new Color(0.04f, 0.8f, 0.28f) * 2f);
            }
            glyphSequenceIndex++;
            if (Settings.spellEffects) SpawnProjectile();
            SetStatus("Glyph accepted: " + candidate.symbol + " — " + glyphSequenceIndex + "/" + glyphTrials.Count, 2.0f);
        }
        else
        {
            glyphSequenceIndex = 0;
            for (int i = 0; i < glyphTrials.Count; i++)
            {
                GlyphTrial glyph = glyphTrials[i];
                glyph.solved = false;
                if (glyph.renderer != null)
                {
                    glyph.renderer.material.color = new Color(0.68f, 0.39f, 1f);
                    glyph.renderer.material.SetColor("_EmissionColor", new Color(0.37f, 0.08f, 0.95f) * 1.8f);
                }
            }
            SetStatus("Wrong glyph order. The sequence resets. Start with " + currentGlyphSequence[0] + ".", 2.4f);
        }
        return true;
    }

    private void TryAnchorEcho()
    {
        if (echoPuzzleSolved)
        {
            SetStatus("PAST and PRESENT are already linked.", 1.8f);
            return;
        }
        if (echoAnchorActive)
        {
            SetStatus("The echo is already anchored. Reach PRESENT before it fades: " + echoTimer.ToString("0.0") + "s", 2.0f);
            return;
        }
        if (!IsPlayerAtSeal(pastSealPosition))
        {
            SetStatus("Z only binds PAST. Stand in the center of the blue PAST seal first.", 2.6f);
            return;
        }
        if (!SpendMana(22f)) return;
        echoAnchorActive = true;
        echoTimer = 8.5f;
        checkpointPosition = Player.transform.position;
        if (echoVisual != null)
        {
            echoVisual.SetActive(true);
            echoVisual.transform.position = pastSealPosition + Vector3.up * 0.9f;
        }
        UpdateSealVisuals();
        SetStatus("ECHO ANCHORED ON PAST. Reach PRESENT before it fades: 8.5 seconds.", 2.8f);
    }

    private void TryChronoBind()
    {
        if (!requiresChrono)
        {
            SetStatus("There is no crumbling Chrono ledge in this chamber.", 1.8f);
            return;
        }
        if (chronoPuzzleSolved)
        {
            SetStatus("The Chrono ledge is stable. Trial complete.", 1.8f);
            return;
        }
        if (chronoPlatform == null || !chronoStarted || !IsPlayerOnPlatform(chronoPlatform) || chronoCollapseTimer <= 0f)
        {
            SetStatus("T must be cast while standing on the orange ledge as it begins to crumble.", 2.6f);
            return;
        }
        if (!SpendMana(20f)) return;
        chronoPuzzleSolved = true;
        chronoBoundTimer = 4.4f;
        chronoCollapseTimer = 0f;
        Collider col = chronoPlatform.GetComponent<Collider>();
        Renderer rend = chronoPlatform.GetComponent<Renderer>();
        if (col != null) col.enabled = true;
        if (rend != null) rend.enabled = true;
        if (chronoWarning != null) chronoWarning.SetActive(false);
        SetStatus("TIME BOUND. The crumbling ledge is frozen; Chrono trial complete.", 2.5f);
    }

    public void NotifyPlayerJump()
    {
        if (stoneStepLanded && requiresCrystal && !highCrystalCollected)
        {
            crystalJumpArmed = true;
            stoneStepLanded = false;
        }
    }

    private void CheckStoneStepLanding()
    {
        if (activeStoneStep == null || Player == null || !activeStoneStep.activeInHierarchy) return;
        Vector3 pos = Player.transform.position;
        Vector3 stepPos = activeStoneStep.transform.position;
        float top = stepPos.y + activeStoneStep.transform.localScale.y * 0.5f;
        if (Mathf.Abs(pos.y - top) < 0.65f && Mathf.Abs(pos.x - stepPos.x) < 1.0f && Mathf.Abs(pos.z - stepPos.z) < 1.0f)
        {
            if (requiresCrystal && !highCrystalCollected) stoneStepLanded = true;
        }
    }

    private bool SpendMana(float amount)
    {
        if (Mana < amount) { SetStatus("Not enough arcane mana.", 1.5f); return false; }
        Mana -= amount;
        return true;
    }

    private void SpawnProjectile()
    {
        if (Player == null) return;
        Transform cam = Player.ViewCamera.transform;
        Material boltMat = new Material(Shader.Find("Standard"));
        boltMat.color = new Color(0.25f, 0.9f, 1f);
        boltMat.EnableKeyword("_EMISSION");
        boltMat.SetColor("_EmissionColor", new Color(0.1f, 0.75f, 1f) * 2.2f);
        GameObject bolt = MakePrimitive(PrimitiveType.Sphere, "Arcane Bolt", cam.position + cam.forward * 0.7f, Vector3.one * 0.18f, boltMat, false, null);
        bolt.AddComponent<ShadowRuinsProjectile>().Initialize(cam.forward);
    }

    private void SpawnStoneStep()
    {
        if (Player == null) return;
        if (activeStoneStep != null) Destroy(activeStoneStep);
        stoneStepLanded = false;
        crystalJumpArmed = false;
        Vector3 forward = Player.ViewCamera.transform.forward;
        Vector3 place = Player.transform.position + forward * 2.55f + Vector3.up * 0.85f;
        place.y = Mathf.Max(place.y, Player.transform.position.y + 0.65f);
        if (requiresCrystal && !highCrystalCollected && highCrystal != null)
        {
            Vector3 delta = highCrystal.transform.position - Player.transform.position;
            if (new Vector2(delta.x, delta.z).magnitude < 5.5f)
                place = new Vector3(highCrystal.transform.position.x - forward.x * 0.65f,
                    Player.transform.position.y + 0.9f, highCrystal.transform.position.z - forward.z * 0.65f);
        }
        activeStoneStep = MakePrimitive(PrimitiveType.Cube, "Temporary Stone Step", place, new Vector3(2.7f, 0.28f, 1.8f), MakeStoneMaterial(new Color(0.45f, 0.49f, 0.52f)), true, levelRoot);
        activeStoneStep.AddComponent<ShadowRuinsTemporaryPlatform>();
    }

    public void SetStatus(string message, float duration)
    {
        StatusMessage = message;
        statusTimers.Clear();
        statusTimers[message] = duration;
    }

    private void TickStatusTimers()
    {
        if (statusTimers.Count == 0) return;
        List<string> keys = new List<string>(statusTimers.Keys);
        foreach (string key in keys)
        {
            statusTimers[key] -= Time.deltaTime;
            if (statusTimers[key] <= 0f) { statusTimers.Remove(key); if (StatusMessage == key) StatusMessage = ""; }
        }
    }

    public string CurrentLevelName() { return LevelNames[(LevelNumber - 1) % LevelNames.Length]; }

    public Material MakeStoneMaterial(Color baseColor)
    {
        int key = baseColor.GetHashCode() ^ (Settings.realism * 971);
        if (materialCache.TryGetValue(key, out Material cached) && cached != null) return cached;
        Shader shader = Shader.Find("Standard");
        Material mat = new Material(shader) { color = baseColor };
        materialBaseColors[mat] = baseColor;
        mat.SetFloat("_Glossiness", Settings.realism == 2 ? 0.18f : (Settings.realism == 1 ? 0.22f : 0.16f));
        mat.SetFloat("_Metallic", 0.02f);
        mat.name = "Procedural Weathered Stone";
        Texture2D tex = BuildStoneTexture(baseColor, Settings.realism);
        tex.name = "Procedural Weathered Stone";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        mat.mainTexture = tex;
        materialCache[key] = mat;
        return mat;
    }

    private Texture2D BuildStoneTexture(Color baseColor, int realism)
    {
        // Classic deliberately keeps the original texture path unchanged.
        // Enhanced/Ultra use a larger, block-laid masonry texture with recessed mortar,
        // worn stone variation, chips and fine fissures.
        if (realism > 0)
            return BuildMasonryStoneTexture(baseColor, realism);

        const int size = 96;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, true);
        float coarseScale = realism == 2 ? 8f : 5.5f;
        float fineScale = realism == 2 ? 28f : 20f;
        float seed = (LevelNumber * 0.731f) + baseColor.r * 4.2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;
                float coarse = Mathf.PerlinNoise(u * coarseScale + seed, v * coarseScale + seed * 0.37f);
                float fine = Mathf.PerlinNoise(u * fineScale + seed * 0.8f, v * fineScale + seed * 0.19f);
                float grain = UnityEngine.Random.value * (realism == 2 ? 0.11f : 0.06f);
                float value = 0.62f + coarse * 0.22f + fine * 0.11f + grain;
                if (realism == 2)
                {
                    float crackNoise = Mathf.PerlinNoise(u * 46f + seed, v * 46f + seed * 2f);
                    if (crackNoise > 0.82f && fine < 0.35f) value *= 0.72f;
                }
                texture.SetPixel(x, y, new Color(value, value * 0.99f, value * 0.965f, 1f));
            }
        }
        texture.Apply(true, false);
        return texture;
    }

    private Texture2D BuildMasonryStoneTexture(Color baseColor, int realism)
    {
        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, true);
        texture.name = realism == 2 ? "Ultra Realistic Chipped Masonry" : "Enhanced Weathered Masonry";
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear;
        float seed = LevelNumber * 0.731f + baseColor.r * 19.7f + baseColor.g * 7.3f;
        int rows = realism == 2 ? 5 : 4;
        int brickW = size / (realism == 2 ? 3 : 4);
        int brickH = size / rows;
        int mortar = realism == 2 ? 4 : 3;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int row = y / brickH;
                int shiftedX = (x + ((row % 2) * brickW / 2)) % size;
                int localX = shiftedX % brickW;
                int localY = y % brickH;
                bool seam = localX < mortar || localY < mortar;
                float u = x / (float)size;
                float v = y / (float)size;
                float broad = Mathf.PerlinNoise(u * (realism == 2 ? 17f : 12f) + seed, v * 13f + seed * 0.37f);
                float grain = Mathf.PerlinNoise(u * 87f + seed * 0.7f, v * 91f + seed * 0.23f);
                float pitting = Mathf.PerlinNoise(u * 43f + seed * 1.8f, v * 47f + seed * 0.9f);
                float value = 0.52f + broad * 0.29f + grain * 0.13f + pitting * 0.08f;
                if (seam) value *= realism == 2 ? 0.43f : 0.52f;
                else
                {
                    // Chipped corners and hairline cracks stay visual only; collision meshes are untouched.
                    int edgeDist = Mathf.Min(localX, brickW - localX, localY, brickH - localY);
                    if (realism == 2 && edgeDist <= 2 && pitting > 0.57f) value *= 0.72f;
                    float crack = Mathf.PerlinNoise(u * 61f + seed * 2.1f, v * 61f + seed * 1.4f);
                    if (realism == 2 && crack > 0.79f && grain < 0.43f) value *= 0.68f;
                }
                value = Mathf.Clamp(value, 0.18f, 1.0f);
                texture.SetPixel(x, y, new Color(value, value * 0.99f, value * 0.97f, 1f));
            }
        }
        texture.Apply(true, false);
        return texture;
    }

    public GameObject MakePrimitive(PrimitiveType type, string objectName, Vector3 position, Vector3 scale, Material material, bool collider, Transform parent)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = objectName;
        obj.transform.position = position;
        obj.transform.localScale = scale;
        if (parent != null) obj.transform.SetParent(parent, true);
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null && material != null) renderer.sharedMaterial = material;
        if (!collider)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }
        return obj;
    }

    private void ApplySettings()
    {
        int preset = Mathf.Clamp(Settings.graphicsPreset, 0, 2);
        if (preset == 0)
        {
            QualitySettings.shadows = Settings.shadows ? ShadowQuality.HardOnly : ShadowQuality.Disable;
            QualitySettings.shadowResolution = ShadowResolution.Low;
            QualitySettings.shadowDistance = Mathf.Min(Settings.viewDistance, 28f);
            QualitySettings.antiAliasing = 0;
            QualitySettings.lodBias = 0.65f;
            QualitySettings.pixelLightCount = 1;
        }
        else if (preset == 1)
        {
            QualitySettings.shadows = Settings.shadows ? ShadowQuality.All : ShadowQuality.Disable;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = Mathf.Min(Settings.viewDistance, 58f);
            QualitySettings.antiAliasing = 2;
            QualitySettings.lodBias = 1f;
            QualitySettings.pixelLightCount = 2;
        }
        else
        {
            QualitySettings.shadows = Settings.shadows ? ShadowQuality.All : ShadowQuality.Disable;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = Settings.viewDistance;
            QualitySettings.antiAliasing = 4;
            QualitySettings.lodBias = 1.5f;
            QualitySettings.pixelLightCount = 4;
        }
        Application.targetFrameRate = Settings.targetFps <= 0 ? -1 : Settings.targetFps;
        if (sun != null) sun.shadows = Settings.shadows ? (preset == 0 ? LightShadows.Hard : LightShadows.Soft) : LightShadows.None;
        RenderSettings.fogDensity = Settings.realism == 2 ? 0.0035f : (Settings.realism == 1 ? 0.005f : 0.006f);
        RenderSettings.ambientLight = Settings.realism == 2 ? new Color(0.37f, 0.39f, 0.42f) : new Color(0.42f, 0.46f, 0.54f);
        if (musicSource != null)
        {
            musicSource.volume = Settings.musicVolume;
            if (!Settings.musicEnabled) musicSource.Pause();
            else if (musicSource.clip != null && !musicSource.isPlaying) musicSource.UnPause();
        }
        ApplyRealismToRoot(levelRoot);
        if (menuCastle != null) ApplyRealismToRoot(menuCastle.transform);
        if (Player != null) Player.ApplySettings(Settings);
    }

    private void ApplyRealismToRoot(Transform root)
    {
        if (root == null) return;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in renderers)
        {
            Material mat = rend.sharedMaterial;
            if (mat == null || !materialBaseColors.TryGetValue(mat, out Color original)) continue;
            float gray = (original.r + original.g + original.b) / 3f;
            Color neutral = new Color(gray * 0.98f, gray * 0.98f, gray * 0.96f, original.a);
            mat.color = Settings.realism == 2 ? Color.Lerp(original, neutral, 0.30f) : Settings.realism == 1 ? Color.Lerp(original, neutral, 0.14f) : original;
            mat.SetFloat("_Glossiness", Settings.realism == 2 ? 0.15f : Settings.realism == 1 ? 0.19f : 0.16f);
        }
    }

    private void LoadSettings()
    {
        Settings.graphicsPreset = PlayerPrefs.GetInt("sr_graphics", 1);
        Settings.realism = PlayerPrefs.GetInt("sr_realism", 0);
        Settings.shadows = PlayerPrefs.GetInt("sr_shadows", 1) != 0;
        Settings.spellEffects = PlayerPrefs.GetInt("sr_effects", 1) != 0;
        Settings.invertMouseY = PlayerPrefs.GetInt("sr_invert_y", 0) != 0;
        Settings.musicEnabled = PlayerPrefs.GetInt("sr_music_enabled", 1) != 0;
        Settings.showFps = PlayerPrefs.GetInt("sr_fps", 0) != 0;
        Settings.mouseSensitivity = PlayerPrefs.GetFloat("sr_sensitivity", 1f);
        Settings.musicVolume = PlayerPrefs.GetFloat("sr_music_volume", 0.5f);
        Settings.viewDistance = PlayerPrefs.GetFloat("sr_view_distance", 100f);
        Settings.targetFps = PlayerPrefs.GetInt("sr_target_fps", 144);
    }

    private void SaveSettings()
    {
        PlayerPrefs.SetInt("sr_graphics", Settings.graphicsPreset);
        PlayerPrefs.SetInt("sr_realism", Settings.realism);
        PlayerPrefs.SetInt("sr_shadows", Settings.shadows ? 1 : 0);
        PlayerPrefs.SetInt("sr_effects", Settings.spellEffects ? 1 : 0);
        PlayerPrefs.SetInt("sr_invert_y", Settings.invertMouseY ? 1 : 0);
        PlayerPrefs.SetInt("sr_music_enabled", Settings.musicEnabled ? 1 : 0);
        PlayerPrefs.SetInt("sr_fps", Settings.showFps ? 1 : 0);
        PlayerPrefs.SetFloat("sr_sensitivity", Settings.mouseSensitivity);
        PlayerPrefs.SetFloat("sr_music_volume", Settings.musicVolume);
        PlayerPrefs.SetFloat("sr_view_distance", Settings.viewDistance);
        PlayerPrefs.SetInt("sr_target_fps", Settings.targetFps);
        PlayerPrefs.Save();
    }

    private void OpenOptions(bool fromPause)
    {
        returnOptionsToPause = fromPause;
        wasPausedBeforeOptions = fromPause;
        Mode = ScreenMode.Options;
        SetCursor(false);
    }

    private void CloseOptions()
    {
        ApplySettings();
        SaveSettings();
        Mode = returnOptionsToPause ? ScreenMode.Paused : ScreenMode.MainMenu;
        if (Mode == ScreenMode.Playing) Mode = ScreenMode.Paused;
        SetCursor(false);
    }

    private void ResetSettings()
    {
        Settings = new GameSettings();
        ApplySettings();
        SaveSettings();
    }

    private void EnsureStyles()
    {
        if (stylesReady) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Clamp(Screen.height / 26, 22, 36), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        titleStyle.normal.textColor = new Color(0.65f, 0.91f, 1f);
        subtitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        subtitleStyle.normal.textColor = new Color(0.80f, 0.86f, 0.93f);
        headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        headingStyle.normal.textColor = new Color(0.65f, 0.91f, 1f);
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        bodyStyle.normal.textColor = new Color(0.86f, 0.90f, 0.96f);
        tinyStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        tinyStyle.normal.textColor = new Color(0.70f, 0.77f, 0.87f);
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold, fixedHeight = 38 };
        stylesReady = true;
    }

    private void OnGUI()
    {
        EnsureStyles();
        switch (Mode)
        {
            case ScreenMode.MainMenu: DrawMainMenu(); break;
            case ScreenMode.Options: DrawOptions(); break;
            case ScreenMode.Paused: DrawPauseMenu(); break;
            case ScreenMode.Loading: DrawLoadingScreen(); break;
            case ScreenMode.Playing: DrawHud(); break;
            case ScreenMode.Victory: DrawVictory(); break;
        }
    }

    private Rect CenteredRect(float width, float height) => new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

    private void DrawMainMenu()
    {
        DrawDimOverlay(0.30f);
        Rect rect = CenteredRect(Mathf.Min(520, Screen.width - 36), 420);
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Space(26);
        GUILayout.Label("SHADOW RUINS", titleStyle, GUILayout.Height(48));
        GUILayout.Label("A 3D FIRST-PERSON PLATFORMING ADVENTURE", subtitleStyle, GUILayout.Height(30));
        GUILayout.Space(24);
        if (GUILayout.Button("PLAY", buttonStyle)) StartGame();
        GUILayout.Space(10);
        if (GUILayout.Button("OPTIONS", buttonStyle)) OpenOptions(false);
        GUILayout.Space(14);
        GUILayout.Label("100 chambers · ancient castle ruins · arcane spells · puzzle trials", subtitleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label("WASD Move  ·  Mouse Look  ·  Space Jump  ·  Esc Pause", tinyStyle);
        GUILayout.Space(16);
        GUILayout.EndArea();
    }

    private void DrawPauseMenu()
    {
        DrawDimOverlay(0.55f);
        Rect rect = CenteredRect(440, 340);
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Space(22);
        GUILayout.Label("PAUSED", titleStyle, GUILayout.Height(48));
        GUILayout.Space(20);
        if (GUILayout.Button("RESUME", buttonStyle)) ResumeGame();
        GUILayout.Space(8);
        if (GUILayout.Button("OPTIONS", buttonStyle)) OpenOptions(true);
        GUILayout.Space(8);
        if (GUILayout.Button("RETURN TO MAIN MENU", buttonStyle)) ReturnToMainMenu();
        GUILayout.Space(14);
        GUILayout.Label("Press Esc to resume", subtitleStyle);
        GUILayout.EndArea();
    }

    private void DrawOptions()
    {
        DrawDimOverlay(0.65f);
        Rect rect = CenteredRect(Mathf.Min(650, Screen.width - 30), Mathf.Min(720, Screen.height - 24));
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Space(12);
        GUILayout.Label("OPTIONS", titleStyle, GUILayout.Height(42));
        optionsScroll = GUILayout.BeginScrollView(optionsScroll, GUILayout.ExpandHeight(true));
        GUILayout.Label("Graphics", headingStyle);
        SettingSelection("Graphics preset", new[] { "Low", "Balanced", "High" }, Settings.graphicsPreset, v => Settings.graphicsPreset = v);
        SettingSelection("Realism", new[] { "Classic", "Enhanced", "Ultra Realistic" }, Settings.realism, v => Settings.realism = v);
        SettingToggle("Shadows", Settings.shadows, v => Settings.shadows = v);
        SettingToggle("Spell visual effects", Settings.spellEffects, v => Settings.spellEffects = v);
        SettingSlider("View / shadow distance", Settings.viewDistance, 25f, 160f, v => Settings.viewDistance = v, Mathf.RoundToInt(Settings.viewDistance).ToString());
        SettingSelection("Frame-rate cap", new[] { "60", "90", "120", "144", "Uncapped" }, Settings.targetFps == 60 ? 0 : Settings.targetFps == 90 ? 1 : Settings.targetFps == 120 ? 2 : Settings.targetFps == 144 ? 3 : 4, v => Settings.targetFps = v == 0 ? 60 : v == 1 ? 90 : v == 2 ? 120 : v == 3 ? 144 : 0);
        GUILayout.Space(12);
        GUILayout.Label("Controls", headingStyle);
        SettingSlider("Mouse sensitivity", Settings.mouseSensitivity, 0.25f, 2.5f, v => Settings.mouseSensitivity = v, Settings.mouseSensitivity.ToString("0.00") + "x");
        SettingToggle("Invert mouse Y", Settings.invertMouseY, v => Settings.invertMouseY = v);
        GUILayout.Space(12);
        GUILayout.Label("Audio and interface", headingStyle);
        SettingToggle("Music enabled", Settings.musicEnabled, v => { Settings.musicEnabled = v; if (musicSource != null) { if (v) { if (!musicSource.isPlaying) musicSource.UnPause(); } else musicSource.Pause(); } });
        SettingSlider("Music volume", Settings.musicVolume, 0f, 1f, v => Settings.musicVolume = v, Mathf.RoundToInt(Settings.musicVolume * 100f) + "%");
        SettingToggle("FPS counter (F3)", Settings.showFps, v => Settings.showFps = v);
        GUILayout.EndScrollView();
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("RESET DEFAULTS", GUILayout.Height(34))) ResetSettings();
        if (GUILayout.Button("BACK", GUILayout.Height(34), GUILayout.Width(150))) CloseOptions();
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        GUILayout.EndArea();
    }

    private void SettingSelection(string label, string[] options, int selected, Action<int> changed)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, bodyStyle, GUILayout.Width(205));
        int next = GUILayout.SelectionGrid(selected, options, options.Length, GUI.skin.button, GUILayout.Height(30));
        GUILayout.EndHorizontal();
        if (next != selected) { changed(next); ApplySettings(); SaveSettings(); }
    }

    private void SettingToggle(string label, bool value, Action<bool> changed)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, bodyStyle, GUILayout.Width(205));
        bool next = GUILayout.Toggle(value, value ? "ON" : "OFF", GUILayout.Width(90));
        GUILayout.EndHorizontal();
        if (next != value) { changed(next); ApplySettings(); SaveSettings(); }
    }

    private void SettingSlider(string label, float value, float min, float max, Action<float> changed, string valueText)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, bodyStyle, GUILayout.Width(205));
        float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(140));
        GUILayout.Label(valueText, tinyStyle, GUILayout.Width(60));
        GUILayout.EndHorizontal();
        if (Mathf.Abs(next - value) > 0.008f) { changed(next); ApplySettings(); SaveSettings(); }
    }

    private void DrawLoadingScreen()
    {
        DrawDimOverlay(0.65f);
        GUILayout.BeginArea(CenteredRect(500, 180), GUI.skin.box);
        GUILayout.FlexibleSpace();
        GUILayout.Label("PREPARING NEXT CHAMBER", titleStyle, GUILayout.Height(52));
        GUILayout.Label("Waiting for the frame rate to exceed 100 FPS...", subtitleStyle, GUILayout.Height(32));
        GUILayout.Label("A safety timeout prevents the game from getting stuck on slower computers.", tinyStyle);
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    private void DrawVictory()
    {
        DrawDimOverlay(0.55f);
        GUILayout.BeginArea(CenteredRect(520, 280), GUI.skin.box);
        GUILayout.FlexibleSpace();
        GUILayout.Label("THE SHADOW RUINS ARE CONQUERED", titleStyle, GUILayout.Height(70));
        GUILayout.Label("You completed all 100 chambers.", subtitleStyle, GUILayout.Height(35));
        GUILayout.Space(12);
        if (GUILayout.Button("RETURN TO MAIN MENU", buttonStyle)) ReturnToMainMenu();
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    private void DrawDimOverlay(float alpha)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0.018f, 0.03f, 0.055f, alpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private string GetHudPuzzleStatus()
    {
        List<string> parts = new List<string>();
        if (glyphTrials.Count > 0 && glyphSequenceIndex < glyphTrials.Count)
            parts.Add("Glyph: " + currentGlyphSequence[glyphSequenceIndex]);
        if (bridgeTrials.Count > 0)
        {
            int done = 0;
            for (int i = 0; i < bridgeTrials.Count; i++) if (bridgeTrials[i].completed) done++;
            parts.Add("Bridges " + done + "/" + bridgeTrials.Count);
        }
        parts.Add(echoPuzzleSolved ? "Echo linked" : echoAnchorActive ? "Reach PRESENT: " + echoTimer.ToString("0.0") + "s" : "Echo: Z at PAST → PRESENT");
        if (requiresCrystal && !highCrystalCollected) parts.Add("High crystal");
        if (requiresChrono && !chronoPuzzleSolved) parts.Add("Chrono ledge");
        if (parts.Count == 0) return "All chamber trials complete";
        string result = string.Join(" · ", parts.ToArray());
        return result.Length > 98 ? result.Substring(0, 95) + "..." : result;
    }

    private void DrawHud()
    {
        GUI.Box(new Rect(14, 14, 500, 102), GUIContent.none);
        GUI.Label(new Rect(28, 22, 470, 24), "LEVEL " + LevelNumber + " / 100 — " + CurrentLevelName(), headingStyle);
        string revealState = revealTimer > 0f ? " (" + revealTimer.ToString("0.0") + "s)" : "";
        string objectiveSummary = "Q Sigils " + SigilsCollected + "/" + SigilsRequired + revealState + " · E Puzzles · R Step · Z Echo · T Chrono";
        GUI.Label(new Rect(28, 48, 470, 24), objectiveSummary, tinyStyle);
        GUI.Label(new Rect(28, 72, 470, 38), GetHudPuzzleStatus(), tinyStyle);
        if (echoAnchorActive && !echoPuzzleSolved) GUI.Label(new Rect((Screen.width - 260) / 2, 18, 260, 24), "ECHO FADES IN " + echoTimer.ToString("0.0") + "s", headingStyle);
        GUI.Box(new Rect(Screen.width - 225, 14, 208, 58), GUIContent.none);
        GUI.Label(new Rect(Screen.width - 211, 22, 190, 22), "ARCANE MANA  " + Mathf.RoundToInt(Mana) + " / 100", bodyStyle);
        GUI.HorizontalSlider(new Rect(Screen.width - 211, 47, 183, 12), Mana, 0, 100);
        if (Settings.showFps) GUI.Label(new Rect(Screen.width - 142, 82, 128, 24), "FPS: " + fpsValue, headingStyle);
        GUI.Label(new Rect(14, Screen.height - 47, Screen.width - 28, 22), "WASD Move · Space Jump · Shift Sprint · C Crouch · Left Click Sword · Esc Pause", bodyStyle);
        if (!string.IsNullOrEmpty(StatusMessage))
        {
            float w = Mathf.Min(700, Screen.width - 50);
            GUI.Box(new Rect((Screen.width - w) / 2, Screen.height - 121, w, 56), GUIContent.none);
            GUI.Label(new Rect((Screen.width - w) / 2 + 10, Screen.height - 118, w - 20, 52), StatusMessage, subtitleStyle);
        }
        // Small crosshair helps target pickups in first-person.
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        GUI.color = new Color(0.82f, 0.95f, 1f, 0.78f);
        GUI.DrawTexture(new Rect(cx - 1f, cy - 5f, 2f, 10f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 5f, cy - 1f, 10f, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}

