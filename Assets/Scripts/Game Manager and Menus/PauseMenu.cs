using UnityEngine;
using UnityEngine.UI;

public class PauseMenu : MonoBehaviour
{
    [SerializeField] GameObject pausePanel; // assign the pause UI panel in Inspector
    [SerializeField] string feedbackFormUrl = "https://forms.gle/YOUR_FORM_ID";
    [SerializeField] Button restartBlightRunButton; // always visible; interactable only once GameManager.Instance.CleanRunCompleted

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
        Cursor.visible = isPaused;
        Cursor.lockState = isPaused ? CursorLockMode.None : CursorLockMode.Locked;

        if (isPaused) restartBlightRunButton.interactable = GameManager.Instance.CleanRunCompleted;
    }

    // Hook to Resume button OnClick
    public void OnResumePressed()
    {
        TogglePause();
    }

    public void OnRestartCleanRunPressed()
    {
        GameManager.Instance.RestartCleanRun();
        if (isPaused) TogglePause();
    }

    public void OnRestartBlightRunPressed()
    {
        GameManager.Instance.RestartBlightRun();
        if (isPaused) TogglePause();
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