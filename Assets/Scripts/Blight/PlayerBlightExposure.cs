using UnityEngine;

// Sibling component to PlayerController, same pattern as JumpMetricsLogger/PlaytestRecorder —
// subscribes to an event, doesn't reach into PlayerController's internals. Owns the exposure
// meter and its death condition; movement punishment (wall-stick skip, ground speed cap) lives
// in PlayerController itself since it has to touch physics directly every FixedUpdate anyway.
public class PlayerBlightExposure : MonoBehaviour
{
    [SerializeField] PlayerController playerController;
    [SerializeField] SpriteRenderer playerSprite;

    [Header("Tuning — rough guesses from the clean-run log, refine per chunk via playtest")]
    [SerializeField] float accumulationRate = 1f; // exposure/sec while touching corrupted ground or wall
    [SerializeField] float decayRate = 1.5f;      // exposure/sec while clear — faster than accumulation, so a brief touch isn't punishing
    [SerializeField] float decayDelay = 1f;     // sec of clean contact required before decay starts
    [SerializeField] float maxExposure = 3f;

    [SerializeField] Color cleanColor = Color.white;
    [SerializeField] Color corruptedColor = new Color(0.6f, 0.1f, 0.8f);
    static readonly int FillAmountID = Shader.PropertyToID("_FillAmount");
    static readonly int CleanColorID = Shader.PropertyToID("_CleanColor");
    static readonly int CorruptedColorID = Shader.PropertyToID("_CorruptedColor");
    MaterialPropertyBlock mpb;

    public float Exposure { get; private set; }
    public float ExposureFraction => maxExposure > 0f ? Exposure / maxExposure : 0f;

    float timeSinceLastCorruption;


    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        playerSprite.GetPropertyBlock(mpb);
        mpb.SetColor(CleanColorID, cleanColor);
        mpb.SetColor(CorruptedColorID, corruptedColor);
        playerSprite.SetPropertyBlock(mpb);
        GameManager.Instance.RegisterBlightExposure(this);
    }

    void Reset()
    {
        playerController = GetComponent<PlayerController>();
        playerSprite = GetComponent<SpriteRenderer>();
    }

    void OnEnable()
    {
        if (playerController != null) playerController.OnCorruptionContact += HandleCorruptionContact;
    }

    void OnDisable()
    {
        if (playerController != null) playerController.OnCorruptionContact -= HandleCorruptionContact;
    }

    // Fires once per FixedUpdate from PlayerController's own ground/wall checks — same frame the
    // corruption state was determined, so there's no cross-component read-order to manage.
    void HandleCorruptionContact(bool groundCorrupted, bool wallCorrupted)
    {
        bool touchingCorruption = groundCorrupted || wallCorrupted;

        if (touchingCorruption)
        {
            timeSinceLastCorruption = 0f;
            Exposure += accumulationRate * Time.fixedDeltaTime;
        }
        else
        {
            timeSinceLastCorruption += Time.fixedDeltaTime;
            if (timeSinceLastCorruption >= decayDelay)
            {
                Exposure -= decayRate * Time.fixedDeltaTime;
            }
        }

        Exposure = Mathf.Clamp(Exposure, 0f, maxExposure);

        if (playerSprite != null)
        {
            playerSprite.GetPropertyBlock(mpb);
            mpb.SetFloat(FillAmountID, ExposureFraction);
            playerSprite.SetPropertyBlock(mpb);
            //playerSprite.color = Color.Lerp(cleanColor, corruptedColor, ExposureFraction);
        }

        if (Exposure >= maxExposure)
        {
            //ResetExposure();
            GameManager.Instance.Die();
        }
    }

    public void ResetExposure()
    {
        Exposure = 0;
    }
}
