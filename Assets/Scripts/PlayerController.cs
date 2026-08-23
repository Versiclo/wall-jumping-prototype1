using UnityEngine;

public enum PlayerStatus {Grounded, Airborne, WallSliding, WallJumping}

public class PlayerController : MonoBehaviour
{
    [SerializeField] PlayerStatus playerStatus;
    Rigidbody2D playerRb;
    InputSystem_Actions controls;
    
    // two transforms on right and left edges of the player that cast ray to check for walls
    [SerializeField] Transform wallCheckpointLeft;
    [SerializeField] Transform wallCheckpointRight;

    [SerializeField] float maxRunSpeed = 5f;
    [SerializeField] float moveForce = 40f;
    [SerializeField] float groundDecel = 60f; // how fast you stop when releasing input, grounded
    [SerializeField] float airDecel = 20f; // slower stop in air feels more natural
    [SerializeField] float jumpSpeed = 12f;
    [SerializeField] float wallJumpSpeedX = 8f;
    [SerializeField] float wallJumpSpeedY = 12f;
    [SerializeField] float fallGravityMultiplier = 2.5f; // extra pull while falling, for a snappy drop
    [SerializeField] float airBorneForceRestriction = 0.6f;
    [SerializeField] float wallJumpLockDuration = 0.2f;
    [SerializeField] float wallCheckDistance = 0.2f;
    float wallJumpLockTimer;
    bool jumpRequested = false;

    bool touchingWallLeft;
    bool touchingWallRight;
    
    void Awake()
    {
        playerRb = GetComponent<Rigidbody2D>();
        controls = new InputSystem_Actions();
        wallCheckpointLeft = GetComponentInChildren<Transform>().Find("Wall Checkpoint Left");
        wallCheckpointRight = GetComponentInChildren<Transform>().Find("Wall Checkpoint Right");
    }

    void OnEnable()
    {
        controls.Player.Enable();
        controls.Player.Jump.performed += OnJumpPerformed;
    }

    void OnDisable()
    {
        controls.Player.Jump.performed -= OnJumpPerformed;
        controls.Player.Disable();
    }

    void OnJumpPerformed(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
    {
        jumpRequested = true; // just raise a flag — don't touch physics here
    }

    void FixedUpdate()
    {
        CheckWalls();
        // move
        Vector2 movementInput = controls.Player.Move.ReadValue<Vector2>();
        bool noInput = Mathf.Approximately(movementInput.x, 0f);

        if (playerStatus != PlayerStatus.WallJumping)
        {
            float force = (playerStatus == PlayerStatus.Grounded) ? moveForce : moveForce * airBorneForceRestriction;
            playerRb.AddForce(Vector2.right * force * movementInput.x);
            // Deceleration when there is no input
            if (noInput)
            {
                float decel = (playerStatus == PlayerStatus.Grounded) ? groundDecel : airDecel;
                playerRb.linearVelocityX = Mathf.MoveTowards(playerRb.linearVelocityX, 0f, decel * Time.fixedDeltaTime);
            }
            // restrict linear velocity on x axis to maxRunSpeed
            playerRb.linearVelocityX = Mathf.Clamp(playerRb.linearVelocityX, -maxRunSpeed, maxRunSpeed);
        }

        // Snappier fall, independent of Gravity Scale
        if (playerRb.linearVelocityY < 0f)
        {
            playerRb.AddForce(Vector2.up * Physics2D.gravity.y * playerRb.gravityScale * (fallGravityMultiplier - 1f) * playerRb.mass);
        }

        if (playerStatus == PlayerStatus.WallJumping)
        {
            wallJumpLockTimer -= Time.fixedDeltaTime;
            if (wallJumpLockTimer <= 0f)
            {
                playerStatus = PlayerStatus.Airborne;
            }
        }

        if (jumpRequested && playerStatus != PlayerStatus.Airborne)
        {
            if (playerStatus == PlayerStatus.Grounded)
            {
                playerRb.linearVelocityY = jumpSpeed;
                playerStatus = PlayerStatus.Airborne;
            }
            else if (playerStatus == PlayerStatus.WallSliding)
            {
                float dirX = (touchingWallLeft) ? wallJumpSpeedX : -wallJumpSpeedX;
                playerRb.linearVelocity = new Vector2(dirX, wallJumpSpeedY);
                playerStatus = PlayerStatus.WallJumping;
                wallJumpLockTimer = wallJumpLockDuration;
            }
        }
        jumpRequested = false; // consume it either way, so stale presses don't fire late

    }

    void CheckWalls()
    {
        RaycastHit2D leftHit = Physics2D.Raycast(wallCheckpointLeft.position, Vector2.left, wallCheckDistance);
        RaycastHit2D rightHit = Physics2D.Raycast(wallCheckpointRight.position, Vector2.right, wallCheckDistance);

        touchingWallLeft = leftHit.collider != null && leftHit.collider.CompareTag("Wall");
        touchingWallRight = rightHit.collider != null && rightHit.collider.CompareTag("Wall");
        bool touchingAnyWall = touchingWallLeft || touchingWallRight;

        // Never let WallJumping be interrupted by wall detection — that's the whole point of the lock
        if (playerStatus == PlayerStatus.WallJumping)
        {
            Debug.Log("WallJumping");
            return;
        }

        if (touchingAnyWall && playerStatus == PlayerStatus.Airborne)
        {
            playerStatus = PlayerStatus.WallSliding;
            Debug.Log("WallSliding");
        }
        else if (!touchingAnyWall && playerStatus == PlayerStatus.WallSliding)
        {
            playerStatus = PlayerStatus.Airborne;
            Debug.Log("Reset");
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            playerStatus = PlayerStatus.Grounded;
        }
    }



}
