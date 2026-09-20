using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    [SerializeField] GameObject pausePanel; // assign the pause UI panel in Inspector
    [SerializeField] string feedbackFormUrl = "https://forms.gle/YOUR_FORM_ID";
    [SerializeField] Vector3 restartPos = new Vector3(-28f, -3.5f, 0);
    [SerializeField] int firstZoneToReset = 0;

    InputSystem_Actions controls;
    bool isPaused = false;

    void Awake()
    {
        controls = new InputSystem_Actions();
    }

    void OnEnable()
    {
        controls.Player.Enable();
    }

    void OnDisable()
    {
        controls.Player.Disable();
    }

    void Update()
    {
        if (controls.Player.PauseMenu.WasPressedThisFrame()) // reused as "Pause" trigger
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        isPaused = !isPaused;
        pausePanel.SetActive(isPaused);
        Time.timeScale = isPaused ? 0f : 1f;
    }

    // Hook to Resume button OnClick
    public void OnResumePressed()
    {
        TogglePause();
    }

    public void OnRestartPressed()
    {
        GameManager.Instance.SetCheckpoint(restartPos, firstZoneToReset);
        GameManager.Instance.Die();
        TogglePause();
    }

    // Hook to Feedback button OnClick
    public void OnFeedbackPressed()
    {
        Application.OpenURL(feedbackFormUrl);
    }

    // Hook to Quit button OnClick
    public void OnQuitPressed()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}