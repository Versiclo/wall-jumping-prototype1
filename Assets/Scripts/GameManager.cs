using UnityEngine;

public class GameManager : MonoBehaviour
{
    InputSystem_Actions controls;

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

    // Update is called once per frame
    void Update()
    {
        //OnQuitPressed();
    }

/*
    void OnQuitPressed()
    {
        if (controls.Player.Quit.WasPressedThisFrame())
        {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
        }
    }
*/

}
