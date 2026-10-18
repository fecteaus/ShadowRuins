using System.Collections.Generic;
using UnityEngine;

/// <summary>CharacterController movement plus a camera-local, elbow-articulated first-person view model.</summary>
[RequireComponent(typeof(CharacterController))]
public class ShadowRuinsPlayer : MonoBehaviour
{
    public Camera ViewCamera { get; private set; }
    public bool ControlsEnabled { get; set; } = true;

    private ShadowRuinsGame game;
    private CharacterController controller;
    private Transform cameraRoot;
    private Transform viewModel;
    private Transform leftShoulder, rightShoulder;
    private Transform leftElbow, rightElbow;
    private Transform leftHand, rightHand;
    private Transform swordPivot;
    private float pitch;
    private float verticalVelocity;
    private float walkClock;
    private float attackTimer;
    private float spellTimer;
    private string currentSpell = "";
    private float cameraHeight = 1.58f;
    private float standingHeight = 1.8f;
    private float crouchHeight = 1.15f;
    private bool crouched;
    private Vector3 spawnPoint;
    private readonly List<Material> modelMaterials = new List<Material>();

    public void Initialize(ShadowRuinsGame owner, CharacterController cc)
    {
        game = owner;
        controller = cc;
        spawnPoint = transform.position;
        BuildCameraAndViewModel();
        ApplySettings(game.Settings);
    }

    private void Update()
    {
        if (controller == null || ViewCamera == null) return;
        float dt = Time.deltaTime;

        if (!ControlsEnabled || game == null || game.Mode != ShadowRuinsGame.ScreenMode.Playing)
        {
            AnimateViewModel(dt, false);
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            return;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float sensitivity = 2.0f * game.Settings.mouseSensitivity;
            // Mouse X/Y axes are configured in ProjectSettings/InputManager.asset.
            // Their 0.1 axis sensitivity is compensated here to preserve the previous feel.
            float dx = Input.GetAxis("Mouse X") * sensitivity;
            float dy = Input.GetAxis("Mouse Y") * sensitivity * (game.Settings.invertMouseY ? 1f : -1f);
            transform.Rotate(Vector3.up * dx * 2.2f, Space.World);
            pitch = Mathf.Clamp(pitch + dy * 2.2f, -86f, 86f);
            cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            if (Input.GetMouseButtonDown(0)) Attack();

            if (Input.GetKeyDown(KeyCode.Q)) game.PerformAction("Q");
            if (Input.GetKeyDown(KeyCode.E)) game.PerformAction("E");
            if (Input.GetKeyDown(KeyCode.R)) game.PerformAction("R");
            if (Input.GetKeyDown(KeyCode.Z)) game.PerformAction("Z");
            if (Input.GetKeyDown(KeyCode.T)) game.PerformAction("T");
        }

        bool wantsCrouch = Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        SetCrouched(wantsCrouch);

        Vector2 moveInput = Vector2.zero;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) moveInput.x -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) moveInput.x += 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) moveInput.y += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) moveInput.y -= 1f;

        Vector3 input = Vector3.ClampMagnitude(new Vector3(moveInput.x, 0f, moveInput.y), 1f);
        bool moving = input.sqrMagnitude > 0.01f;
        bool sprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float speed = sprinting && !crouched ? 9.2f : (crouched ? 2.7f : 6.7f);
        Vector3 move = (transform.right * input.x + transform.forward * input.z) * speed;
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        if (Input.GetKeyDown(KeyCode.Space) && controller.isGrounded && !crouched)
        {
            verticalVelocity = 8.4f;
            if (game != null) game.NotifyPlayerJump();
        }
        verticalVelocity += -21f * dt;
        move.y = verticalVelocity;
        controller.Move(move * dt);
        AnimateViewModel(dt, moving);
    }

    public void Teleport(Vector3 worldPosition)
    {
        if (controller != null) controller.enabled = false;
        transform.position = worldPosition;
        if (controller != null) controller.enabled = true;
        verticalVelocity = 0f;
    }

    public void ApplySettings(ShadowRuinsGame.GameSettings settings)
    {
        if (ViewCamera != null)
        {
            ViewCamera.farClipPlane = settings.viewDistance * 1.75f;
            ViewCamera.fieldOfView = settings.realism == 2 ? 72f : 76f;
        }
        ApplyRealism(settings.realism);
    }

    public void ApplyRealism(int realism)
    {
        Color sleeve = realism == 2 ? new Color(0.105f, 0.094f, 0.083f) : realism == 1 ? new Color(0.15f, 0.13f, 0.12f) : new Color(0.20f, 0.18f, 0.16f);
        Color skin = realism == 2 ? new Color(0.55f, 0.34f, 0.23f) : new Color(0.68f, 0.46f, 0.34f);
        foreach (Material material in modelMaterials)
        {
            if (material == null) continue;
            if (material.name.Contains("Sleeve")) material.color = sleeve;
            else if (material.name.Contains("Skin")) material.color = skin;
        }
    }

    public void Attack()
    {
        if (attackTimer > 0.03f) return;
        attackTimer = 0.42f;
    }

    public void PlaySpellGesture(string key)
    {
        spellTimer = 0.52f;
        currentSpell = key;
    }

    private void BuildCameraAndViewModel()
    {
        GameObject camObj = new GameObject("First Person Camera");
        camObj.tag = "MainCamera";
        camObj.transform.SetParent(transform, false);
        camObj.transform.localPosition = new Vector3(0f, cameraHeight, 0f);
        cameraRoot = camObj.transform;
        ViewCamera = camObj.AddComponent<Camera>();
        camObj.AddComponent<AudioListener>();
        ViewCamera.clearFlags = CameraClearFlags.SolidColor;
        ViewCamera.backgroundColor = new Color(0.20f, 0.27f, 0.38f);
        ViewCamera.fieldOfView = 76f;
        ViewCamera.nearClipPlane = 0.025f;
        ViewCamera.farClipPlane = 180f;
        ViewCamera.depth = 1f;
        viewModel = new GameObject("First Person Arms and Sword").transform;
        viewModel.SetParent(cameraRoot, false);

        Material sleeve = CreateMaterial("Sleeve Cloth", new Color(0.17f, 0.15f, 0.13f), 0.9f, 0f);
        Material cuff = CreateMaterial("Sleeve Cuff", new Color(0.28f, 0.23f, 0.19f), 0.8f, 0f);
        Material skin = CreateMaterial("Skin", new Color(0.68f, 0.46f, 0.34f), 0.64f, 0f);
        Material skinLight = CreateMaterial("Skin Highlight", new Color(0.75f, 0.53f, 0.4f), 0.56f, 0f);
        Material nail = CreateMaterial("Nail", new Color(0.72f, 0.56f, 0.46f), 0.4f, 0f);
        Material steel = CreateMaterial("Sword Steel", new Color(0.62f, 0.70f, 0.75f), 0.24f, 0.72f);
        Material edge = CreateMaterial("Sword Edge", new Color(0.88f, 0.93f, 0.94f), 0.2f, 0.8f);
        Material leather = CreateMaterial("Sword Leather", new Color(0.12f, 0.075f, 0.045f), 0.92f, 0f);

        leftShoulder = MakeArmChain(-1, sleeve, cuff, skin, skinLight, nail, false);
        rightShoulder = MakeArmChain(1, sleeve, cuff, skin, skinLight, nail, true);
        leftElbow = leftShoulder.Find("ElbowPivot");
        rightElbow = rightShoulder.Find("ElbowPivot");
        leftHand = leftShoulder.Find("ElbowPivot/Hand");
        rightHand = rightShoulder.Find("ElbowPivot/Hand");
        swordPivot = new GameObject("SwordPivot").transform;
        swordPivot.SetParent(rightHand, false);
        swordPivot.localPosition = new Vector3(-0.018f, 0.005f, 0.035f);
        MakeSword(swordPivot, steel, edge, leather);
    }

    private Transform MakeArmChain(int side, Material sleeve, Material cuff, Material skin, Material skinLight, Material nail, bool swordArm)
    {
        Transform shoulder = new GameObject(swordArm ? "Sword Arm Shoulder" : "Open Hand Shoulder").transform;
        shoulder.SetParent(viewModel, false);
        shoulder.localPosition = new Vector3(side * 0.50f, -0.29f, 0.68f);
        shoulder.localRotation = Quaternion.Euler(0f, 0f, side == -1 ? 23f : -23f);

        // Upper arm is its own cylinder, ending at the elbow pivot.
        MakePrimitiveChild(PrimitiveType.Cylinder, "Upper Arm Cylinder", shoulder, new Vector3(0f, -0.205f, 0f), new Vector3(0.235f, 0.215f, 0.235f), sleeve);
        Transform elbow = new GameObject("ElbowPivot").transform;
        elbow.SetParent(shoulder, false);
        elbow.localPosition = new Vector3(0f, -0.425f, 0f);
        MakePrimitiveChild(PrimitiveType.Sphere, "Elbow Joint", elbow, Vector3.zero, new Vector3(0.20f, 0.17f, 0.20f), sleeve);

        // Forearm is a second, distinct cylinder; it rotates at the elbow instead of being welded to the upper arm.
        MakePrimitiveChild(PrimitiveType.Cylinder, "Forearm Cylinder", elbow, new Vector3(0f, -0.205f, 0f), new Vector3(0.205f, 0.215f, 0.205f), sleeve);
        MakePrimitiveChild(PrimitiveType.Cylinder, "Wrist Cuff", elbow, new Vector3(0f, -0.395f, -0.006f), new Vector3(0.225f, 0.052f, 0.225f), cuff);

        Transform hand = new GameObject("Hand").transform;
        hand.SetParent(elbow, false);
        hand.localPosition = new Vector3(0f, -0.43f, -0.005f);
        MakePrimitiveChild(PrimitiveType.Cube, "Palm", hand, new Vector3(0f, -0.015f, 0f), new Vector3(0.15f, 0.105f, 0.072f), skin);

        if (swordArm)
        {
            // Grip fingers are placed on the camera-facing side of the palm and curve toward the handle.
            for (int i = 0; i < 4; i++)
            {
                float x = -0.052f + i * 0.034f;
                Transform finger = new GameObject("Grip Finger " + i).transform;
                finger.SetParent(hand, false);
                finger.localPosition = new Vector3(x, -0.026f, -0.048f);
                finger.localRotation = Quaternion.Euler(-14f, 0f, -10f + i * 6f);
                MakePrimitiveChild(PrimitiveType.Capsule, "Grip Finger Segment", finger, new Vector3(0f, -0.034f, 0f), new Vector3(0.035f, 0.035f, 0.035f), skinLight);
                MakePrimitiveChild(PrimitiveType.Sphere, "Finger Nail", finger, new Vector3(0f, -0.061f, -0.008f), new Vector3(0.022f, 0.012f, 0.009f), nail);
            }
            Transform thumb = new GameObject("Grip Thumb").transform;
            thumb.SetParent(hand, false);
            thumb.localPosition = new Vector3(-side * 0.071f, -0.002f, -0.035f);
            thumb.localRotation = Quaternion.Euler(-16f, 0f, side * -42f);
            MakePrimitiveChild(PrimitiveType.Capsule, "Thumb Segment", thumb, new Vector3(0f, -0.025f, 0f), new Vector3(0.04f, 0.04f, 0.04f), skinLight);
        }
        else
        {
            // The open hand has four fingers attached at the distal edge of the palm.
            // The fingertips run down and inward from the knuckles, not out from the wrist edge.
            for (int i = 0; i < 4; i++)
            {
                float x = -0.052f + i * 0.034f;
                Transform finger = new GameObject("Finger " + (i + 1)).transform;
                finger.SetParent(hand, false);
                finger.localPosition = new Vector3(x, -0.060f, -0.006f);
                finger.localRotation = Quaternion.Euler(0f, 0f, (i - 1.5f) * -3f);
                MakePrimitiveChild(PrimitiveType.Capsule, "Proximal Finger", finger, new Vector3(0f, -0.032f, 0f), new Vector3(0.034f, 0.036f, 0.034f), skinLight);
                Transform joint = new GameObject("Finger Joint").transform;
                joint.SetParent(finger, false);
                joint.localPosition = new Vector3(0f, -0.065f, 0f);
                MakePrimitiveChild(PrimitiveType.Sphere, "Knuckle", joint, Vector3.zero, new Vector3(0.031f, 0.027f, 0.031f), skin);
                MakePrimitiveChild(PrimitiveType.Capsule, "Distal Finger", joint, new Vector3(0f, -0.035f, 0f), new Vector3(0.027f, 0.034f, 0.027f), skinLight);
                MakePrimitiveChild(PrimitiveType.Sphere, "Fingernail", joint, new Vector3(0f, -0.054f, -0.009f), new Vector3(0.019f, 0.010f, 0.007f), nail);
            }
            Transform thumb = new GameObject("Thumb").transform;
            thumb.SetParent(hand, false);
            thumb.localPosition = new Vector3(-side * 0.074f, -0.002f, -0.022f);
            thumb.localRotation = Quaternion.Euler(0f, 0f, side * 39f);
            MakePrimitiveChild(PrimitiveType.Capsule, "Thumb Base", thumb, new Vector3(-side * 0.018f, -0.027f, 0f), new Vector3(0.04f, 0.042f, 0.04f), skinLight);
            MakePrimitiveChild(PrimitiveType.Sphere, "Thumb Tip", thumb, new Vector3(-side * 0.034f, -0.052f, -0.006f), new Vector3(0.034f, 0.032f, 0.031f), skin);
            MakePrimitiveChild(PrimitiveType.Sphere, "Thumb Nail", thumb, new Vector3(-side * 0.036f, -0.057f, -0.024f), new Vector3(0.021f, 0.012f, 0.008f), nail);
        }
        return shoulder;
    }

    private void MakeSword(Transform parent, Material steel, Material edge, Material leather)
    {
        Transform grip = new GameObject("Leather Wrapped Grip").transform;
        grip.SetParent(parent, false);
        grip.localPosition = new Vector3(0f, -0.04f, 0f);
        MakePrimitiveChild(PrimitiveType.Cylinder, "Grip", grip, Vector3.zero, new Vector3(0.035f, 0.085f, 0.035f), leather);
        for (int i = 0; i < 5; i++)
        {
            Transform wrap = new GameObject("Grip Wrap").transform;
            wrap.SetParent(grip, false);
            wrap.localPosition = new Vector3(0f, -0.064f + i * 0.028f, 0f);
            MakePrimitiveChild(PrimitiveType.Cylinder, "Leather Wrap Band", wrap, Vector3.zero, new Vector3(0.037f, 0.008f, 0.037f), cuffMaterialForSword(leather));
        }
        MakePrimitiveChild(PrimitiveType.Sphere, "Pommel", parent, new Vector3(0f, -0.135f, 0f), new Vector3(0.07f, 0.05f, 0.05f), steel);
        MakePrimitiveChild(PrimitiveType.Cube, "Crossguard", parent, new Vector3(0f, 0.01f, 0f), new Vector3(0.28f, 0.045f, 0.07f), steel);
        // Beveled silhouette made from a hand-built mesh with a tapered point.
        GameObject blade = new GameObject("Tapered Steel Blade");
        blade.transform.SetParent(parent, false);
        blade.transform.localPosition = new Vector3(0f, 0.48f, 0f);
        blade.transform.localRotation = Quaternion.Euler(0f, 0f, 7f);
        blade.transform.localScale = Vector3.one;
        MeshFilter filter = blade.AddComponent<MeshFilter>();
        MeshRenderer renderer = blade.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = steel;
        filter.sharedMesh = BuildBladeMesh();
        MakePrimitiveChild(PrimitiveType.Cube, "Blade Highlight Edge", parent, new Vector3(-0.052f, 0.48f, -0.016f), new Vector3(0.012f, 0.63f, 0.008f), edge);
    }

    private Material cuffMaterialForSword(Material leather) { return leather; }

    private Mesh BuildBladeMesh()
    {
        Mesh mesh = new Mesh { name = "Tapered Sword Blade" };
        Vector3[] v = {
            new Vector3(-0.065f, 0f, -0.018f), new Vector3(0.065f, 0f, -0.018f),
            new Vector3(-0.052f, 0.62f, -0.014f), new Vector3(0.052f, 0.62f, -0.014f),
            new Vector3(0f, 0.84f, 0f),
            new Vector3(-0.065f, 0f, 0.018f), new Vector3(0.065f, 0f, 0.018f),
            new Vector3(-0.052f, 0.62f, 0.014f), new Vector3(0.052f, 0.62f, 0.014f),
            new Vector3(0f, 0.84f, 0f)
        };
        int[] t = {
            0,2,1, 1,2,3, 2,4,3,
            5,6,7, 6,8,7, 7,8,9,
            0,5,7, 0,7,2, 1,3,8, 1,8,6,
            2,7,9, 2,9,4, 3,4,9, 3,9,8,
            0,1,6, 0,6,5
        };
        mesh.vertices = v;
        mesh.triangles = t;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AnimateViewModel(float dt, bool moving)
    {
        if (viewModel == null || leftShoulder == null || rightShoulder == null) return;
        walkClock += dt * (moving ? 10f : 2.3f);
        attackTimer = Mathf.Max(0f, attackTimer - dt);
        spellTimer = Mathf.Max(0f, spellTimer - dt);
        float bob = Mathf.Sin(walkClock) * (moving ? 0.035f : 0.006f);
        float attack = attackTimer > 0f ? Mathf.Sin((1f - attackTimer / 0.42f) * Mathf.PI) : 0f;
        leftShoulder.localRotation = Quaternion.Euler(0f, 0f, 23f + bob * 130f + (spellTimer > 0f ? 16f : 0f));
        rightShoulder.localRotation = Quaternion.Euler(0f, 0f, -23f - bob * 130f - attack * 45f);
        if (leftElbow != null) leftElbow.localRotation = Quaternion.Euler(-5f, 0f, -10f + bob * 130f + (spellTimer > 0f ? 28f : 0f));
        if (rightElbow != null) rightElbow.localRotation = Quaternion.Euler(-5f - attack * 22f, 0f, 8f + bob * 100f + attack * 28f);
        if (swordPivot != null)
        {
            swordPivot.localRotation = Quaternion.Euler(-attack * 25f, 0f, -attack * 70f);
            if (spellTimer > 0f && currentSpell == "R") swordPivot.localRotation = Quaternion.Slerp(swordPivot.localRotation, Quaternion.Euler(-18f, 0f, -12f), 0.2f);
        }
        Vector3 p = viewModel.localPosition;
        p.y = bob * 0.35f;
        viewModel.localPosition = p;
        cameraRoot.localPosition = new Vector3(0f, cameraHeight + bob * 0.4f, 0f);
    }

    private void SetCrouched(bool value)
    {
        if (crouched == value) return;
        crouched = value;
        controller.height = crouched ? crouchHeight : standingHeight;
        controller.center = new Vector3(0f, controller.height * 0.5f, 0f);
        cameraHeight = crouched ? 1.03f : 1.58f;
    }

    private Material CreateMaterial(string materialName, Color color, float roughness, float metallic)
    {
        Material m = new Material(Shader.Find("Standard"));
        m.name = materialName;
        m.color = color;
        m.SetFloat("_Glossiness", 1f - roughness);
        m.SetFloat("_Metallic", metallic);
        modelMaterials.Add(m);
        return m;
    }

    private GameObject MakePrimitiveChild(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPosition;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = localScale;
        Collider collider = obj.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        return obj;
    }
}
