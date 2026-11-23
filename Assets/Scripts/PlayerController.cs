using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;

    private Rigidbody2D rb;
    private Animator animator;
    private InputAction moveAction;

    private Vector2 moveInput;

    private bool hasTarget = false;
    private Vector2 targetPosition;
    private float currentStopDistance = 0.1f;
    private Action onArrivedCallback;

    public event Action OnManualMove;

    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int IsMovingHash = Animator.StringToHash("isMoving");
    private static readonly int AttackHash = Animator.StringToHash("Attack");

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/s")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/a")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");

        moveAction.AddBinding("<Gamepad>/leftStick");
    }

    private void OnEnable() => moveAction.Enable();
    private void OnDisable() => moveAction.Disable();

    private void Start()
    {
        animator.SetFloat(MoveXHash, 0);
        animator.SetFloat(MoveYHash, -1);
    }

    private void Update()
    {
        Vector2 manualInput = moveAction.ReadValue<Vector2>();

        if (manualInput.sqrMagnitude > 0.01f)
        {
            if (hasTarget) StopAutoMove();
            OnManualMove?.Invoke();

            moveInput = manualInput;
            if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();
        }
        else if (hasTarget)
        {
            Vector2 currentPos = transform.position;
            Vector2 diff = targetPosition - currentPos;

            if (diff.magnitude <= currentStopDistance)
            {
                hasTarget = false;
                moveInput = Vector2.zero;

                Action callback = onArrivedCallback;
                onArrivedCallback = null;
                callback?.Invoke();
            }
            else
            {
                moveInput = diff.normalized;
            }
        }
        else moveInput = Vector2.zero;
        bool isMoving = moveInput.sqrMagnitude > 0.01f;
        animator.SetBool(IsMovingHash, isMoving);

        if (isMoving)
        {
            animator.SetFloat(MoveXHash, moveInput.x);
            animator.SetFloat(MoveYHash, moveInput.y);
        }
    }

    private void FixedUpdate()
    {
        rb.linearVelocity = moveInput * moveSpeed;
    }
    public void MoveTo(Vector2 destination, float stopDistance = 0.1f, Action onArrived = null)
    {
        targetPosition = destination;
        currentStopDistance = stopDistance;
        onArrivedCallback = onArrived;
        hasTarget = true;
    }

    public void StopAutoMove()
    {
        hasTarget = false;
        onArrivedCallback = null;
        moveInput = Vector2.zero;
    }

    public void PlayAttackAnimation()
    {
        animator.SetTrigger(AttackHash);
    }
}