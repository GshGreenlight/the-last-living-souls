using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Simple fail state: freeze player actions + show overlay.
/// Wire references in the Editor (Canvas / RigidbodyFPS).
/// </summary>
public class GameOverController : MonoBehaviour
{
    [Header("UI")]
    public GameObject gameOverRoot;
    public Text gameOverLabel;

    [Header("Player freeze")]
    public RigidbodyFPS playerMovement;
    public Behaviour[] extraBehavioursToDisable;

    [Header("Input to disable")]
    public InputActionAsset inputActions;

    private bool _triggered;

    public bool IsGameOver => _triggered;

    public void Trigger()
    {
        if (_triggered)
            return;

        _triggered = true;

        if (gameOverRoot != null)
            gameOverRoot.SetActive(true);

        if (gameOverLabel != null)
            gameOverLabel.text = "GAME OVER";

        if (playerMovement != null)
            playerMovement.enabled = false;

        if (extraBehavioursToDisable != null)
        {
            foreach (var b in extraBehavioursToDisable)
            {
                if (b != null)
                    b.enabled = false;
            }
        }

        if (inputActions != null)
            inputActions.Disable();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("GAME OVER");
    }
}
