using UnityEngine;
using UniVRM10;

public class MouseLookAtController : MonoBehaviour
{
    [SerializeField] private Vrm10Instance vrmInstance;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float fallbackTargetDistance = 2.0f;
    [SerializeField] private float followSpeed = 6f;
    [SerializeField] private bool enableHeadAndEyeTracking = true;
    [SerializeField] private bool lockLookAtTargetEveryFrame = true;
    [SerializeField] private float horizontalRange = 0.45f;
    [SerializeField] private float verticalRange = 0.32f;
    [SerializeField] private bool lookForwardWhileSpeaking = true;
    [SerializeField] private AudioSource speechAudioSource;
    [SerializeField] private bool useHeadRotationAssist = true;
    [SerializeField] private float headYawMax = 18f;
    [SerializeField] private float headPitchMax = 14f;
    [SerializeField] private bool useEyeExpressionAssist = true;
    [SerializeField] private float eyeAssistMaxWeight = 0.7f;
    [SerializeField] private bool enableAutoBlink = true;
    [SerializeField] private Vector2 blinkIntervalRange = new Vector2(2.4f, 5.2f);
    [SerializeField] private float blinkCloseDuration = 0.055f;
    [SerializeField] private float blinkOpenDuration = 0.085f;
    [SerializeField] private float longBlinkChance = 0.12f;
    [SerializeField] private float longBlinkMultiplier = 1.8f;

    private Transform lookAtTarget;
    private Transform headTransform;
    private Quaternion initialHeadLocalRotation;
    private float currentYaw;
    private float currentPitch;
    private float nextBlinkTime;
    private float blinkTimer;
    private float currentBlinkCloseDuration;
    private float currentBlinkOpenDuration;

    private void Awake()
    {
        if (vrmInstance == null)
        {
            vrmInstance = GetComponent<Vrm10Instance>();
            if (vrmInstance == null)
            {
                vrmInstance = FindAnyObjectByType<Vrm10Instance>();
            }
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
        if (speechAudioSource == null)
        {
            speechAudioSource = GetComponent<AudioSource>();
            if (speechAudioSource == null)
            {
                speechAudioSource = FindAnyObjectByType<AudioSource>();
            }
        }

        var targetObject = new GameObject("MouseLookAtTarget");
        lookAtTarget = targetObject.transform;
        lookAtTarget.position = transform.position + transform.forward * fallbackTargetDistance;

        if (vrmInstance != null)
        {
            vrmInstance.TryGetBoneTransform(HumanBodyBones.Head, out headTransform);
            if (headTransform != null)
            {
                initialHeadLocalRotation = headTransform.localRotation;
            }
        }

        ScheduleNextBlink();
    }

    private void OnEnable()
    {
        ApplyLookAtSettings();
    }

    private void Update()
    {
        if (!enableHeadAndEyeTracking) return;
        if (vrmInstance == null || targetCamera == null || lookAtTarget == null) return;

        var targetPosition = (lookForwardWhileSpeaking && IsSpeaking())
            ? ResolveForwardTargetPosition()
            : ResolveTargetPositionOnHeadPlane();
        lookAtTarget.position = Vector3.Lerp(lookAtTarget.position, targetPosition, followSpeed * Time.deltaTime);

        if (lockLookAtTargetEveryFrame)
        {
            ApplyLookAtSettings();
        }
    }

    private void LateUpdate()
    {
        ApplyAutoBlink();
        if (!useHeadRotationAssist) return;
        if (headTransform == null || targetCamera == null) return;

        var vp = targetCamera.ScreenToViewportPoint(Input.mousePosition);
        float nx = Mathf.Clamp((vp.x - 0.5f) * 2f, -1f, 1f);
        float ny = Mathf.Clamp((vp.y - 0.5f) * 2f, -1f, 1f);

        if (lookForwardWhileSpeaking && IsSpeaking())
        {
            nx = 0f;
            ny = 0f;
        }

        // Head bone local axis is opposite to viewport X in this avatar rig.
        float targetYaw = -nx * headYawMax;
        float targetPitch = -ny * headPitchMax;
        currentYaw = Mathf.Lerp(currentYaw, targetYaw, followSpeed * Time.deltaTime);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, followSpeed * Time.deltaTime);

        var assistRotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        headTransform.localRotation = initialHeadLocalRotation * assistRotation;

        ApplyEyeExpressionAssist(nx, ny);
    }

    private void ApplyLookAtSettings()
    {
        if (vrmInstance == null || lookAtTarget == null) return;
        vrmInstance.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;
        vrmInstance.LookAtTarget = lookAtTarget;
    }

    private float ResolveTargetDistance()
    {
        if (targetCamera == null) return Mathf.Max(0.2f, fallbackTargetDistance);
        if (headTransform == null) return Mathf.Max(0.2f, fallbackTargetDistance);

        float d = Vector3.Distance(targetCamera.transform.position, headTransform.position);
        return Mathf.Max(0.2f, d);
    }

    private Vector3 ResolveTargetPositionOnHeadPlane()
    {
        if (targetCamera == null)
        {
            return lookAtTarget.position;
        }
        float distance = ResolveTargetDistance();
        var center = (headTransform != null)
            ? headTransform.position + headTransform.forward * 0.25f
            : transform.position + transform.forward * distance;

        // Viewport (0..1) -> normalized (-1..1).
        var vp = targetCamera.ScreenToViewportPoint(Input.mousePosition);
        float nx = Mathf.Clamp((vp.x - 0.5f) * 2f, -1f, 1f);
        float ny = Mathf.Clamp((vp.y - 0.5f) * 2f, -1f, 1f);

        var offset = (targetCamera.transform.right * (nx * horizontalRange))
            + (targetCamera.transform.up * (ny * verticalRange));
        return center + offset;
    }

    private Vector3 ResolveForwardTargetPosition()
    {
        if (headTransform == null)
        {
            return transform.position + transform.forward * Mathf.Max(0.2f, fallbackTargetDistance);
        }
        return headTransform.position + headTransform.forward * 1.5f;
    }

    private bool IsSpeaking()
    {
        return speechAudioSource != null && speechAudioSource.isPlaying;
    }

    private void ApplyEyeExpressionAssist(float nx, float ny)
    {
        if (!useEyeExpressionAssist) return;
        if (vrmInstance == null) return;

        var exp = vrmInstance.Runtime.Expression;
        float right = Mathf.Clamp01(-nx) * eyeAssistMaxWeight;
        float left = Mathf.Clamp01(nx) * eyeAssistMaxWeight;
        float up = Mathf.Clamp01(ny) * eyeAssistMaxWeight;
        float down = Mathf.Clamp01(-ny) * eyeAssistMaxWeight;

        exp.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.lookRight), right);
        exp.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.lookLeft), left);
        exp.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.lookUp), up);
        exp.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.lookDown), down);
    }

    private void ApplyAutoBlink()
    {
        if (!enableAutoBlink) return;
        if (vrmInstance == null) return;

        blinkTimer += Time.deltaTime;
        float weight = 0f;
        if (blinkTimer >= nextBlinkTime)
        {
            float t = blinkTimer - nextBlinkTime;
            float cycle = currentBlinkCloseDuration + currentBlinkOpenDuration;
            if (t <= cycle)
            {
                if (t <= currentBlinkCloseDuration)
                {
                    weight = Mathf.Clamp01(t / Mathf.Max(0.001f, currentBlinkCloseDuration));
                }
                else
                {
                    float openT = (t - currentBlinkCloseDuration) / Mathf.Max(0.001f, currentBlinkOpenDuration);
                    weight = 1f - Mathf.Clamp01(openT);
                }
            }
            else
            {
                ScheduleNextBlink();
            }
        }

        vrmInstance.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.blink), weight);
    }

    private void ScheduleNextBlink()
    {
        blinkTimer = 0f;
        float minInterval = Mathf.Max(0.5f, Mathf.Min(blinkIntervalRange.x, blinkIntervalRange.y));
        float maxInterval = Mathf.Max(minInterval, Mathf.Max(blinkIntervalRange.x, blinkIntervalRange.y));
        nextBlinkTime = Random.Range(minInterval, maxInterval);

        float scale = Random.value < Mathf.Clamp01(longBlinkChance)
            ? Mathf.Max(1f, longBlinkMultiplier)
            : 1f;
        currentBlinkCloseDuration = Mathf.Max(0.02f, blinkCloseDuration * scale);
        currentBlinkOpenDuration = Mathf.Max(0.02f, blinkOpenDuration * scale);
    }
}
