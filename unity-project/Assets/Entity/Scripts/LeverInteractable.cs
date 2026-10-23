using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Prototype lever: player in radius + Interact → notify entity (distract from chase).
/// Wire references in the Editor.
/// </summary>
public class LeverInteractable : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public EntityBrain entity;
    public InputActionReference interactAction;

    [Header("Range")]
    public float interactRadius = 2f;

    [Header("Visual")]
    public Transform handle;
    public Vector3 restLocalEuler = Vector3.zero;
    public Vector3 pulledLocalEuler = new Vector3(0f, 0f, -45f);
    public float handleLerpSpeed = 8f;

    [Header("Distract")]
    public float inspectSeconds = 2.5f;

    [Header("Events")]
    public UnityEvent onActivated;

    [Header("Debug")]
    public bool drawGizmos = true;

    private bool _pulled;
    private float _handleT;

    private void OnEnable()
    {
        // Enable only — do not Disable here (other levers / systems may share the action).
        interactAction?.action.Enable();
    }

    private void Update()
    {
        UpdateHandleVisual();

        if (player == null || interactAction == null || interactAction.action == null)
            return;

        if (!IsPlayerInRange())
            return;

        // Prefer press edge; works even if the action asset uses a Hold interaction.
        if (!interactAction.action.WasPressedThisFrame())
            return;

        Activate();
    }

    public bool IsPlayerInRange()
    {
        if (player == null)
            return false;

        Vector3 a = transform.position;
        Vector3 b = player.position;
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b) <= interactRadius;
    }

    public void Activate()
    {
        _pulled = !_pulled;
        onActivated?.Invoke();

        if (entity != null)
            entity.DistractToward(transform, inspectSeconds);
        else
            Debug.LogWarning($"[Lever] {name}: EntityBrain not assigned", this);
    }

    private void UpdateHandleVisual()
    {
        if (handle == null)
            return;

        float target = _pulled ? 1f : 0f;
        _handleT = Mathf.MoveTowards(_handleT, target, handleLerpSpeed * Time.deltaTime);
        handle.localEulerAngles = Vector3.Lerp(restLocalEuler, pulledLocalEuler, _handleT);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}
