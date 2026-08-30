using NUnit.Framework;
using UnityEngine;

// wallJumping status keeps the player from sticking to another wall for a certan duration (wallJumpLockDuration)
public enum PlayerStatus {Grounded, Airborne, WallSliding, WallJumping}

public class PlayerController : MonoBehaviour
{
    [SerializeField] PlayerStatus playerStatus;
    Rigidbody2D playerRb;
    InputSystem_Actions controls;
    
    // two transforms on right and left edges of the player that cast ray to check for walls
    [SerializeField] Transform wallCheckpointLeft;
    [SerializeField] Transform wallCheckpointRight;
    [SerializeField] Transform groundCheckpointLeft;
    [SerializeField] Transform groundCheckpointRight;
    // this layer mask holds both ground and wall, and is used for both CheckWalls() and CheckGround() for now
    [SerializeField] LayerMask groundLayer;

    [SerializeField] float maxRunSpeed = 5f;
    [SerializeField] float moveForce = 40f;
    [SerializeField] float groundDecel = 60f; // how fast you stop when releasing input, grounded
    [SerializeField] float airDecel = 20f; // slower stop in air feels more natural
    [SerializeField] float jumpCutMultiplier = 0.5f;
    [SerializeField] float jumpSpeed = 12f;
    [SerializeField] float wallJumpSpeedX = 8f;
    [SerializeField] float wallJumpSpeedY = 12f;
    [SerializeField] float fallGravityMultiplier = 2.5f; // extra pull while falling, for a snappy drop
    [SerializeField] float airBorneForceRestriction = 0.6f;
    [SerializeField] float wallJumpLockDuration = 0.2f;
    [SerializeField] float wallCheckDistance = 0.2f;
    [SerializeField] float wallStickDuration = 0.5f;
    [SerializeField] float groundCheckDistance = 0.1f;
    float wallJumpLockTimer;
    float jumpTimer;
    float wallStickTimer;
    // this variable reads and holds the initial gravity scale for restoration
    float defaultGravityScale;
    bool jumpRequested = false;

    bool touchingWallLeft;
    bool touchingWallRight;
    bool isGrounded;
    
    void Awake()
    {
        playerRb = GetComponent<Rigidbody2D>();
        controls = new InputSystem_Actions();
        wallCheckpointLeft = GetComponentInChildren<Transform>().Find("Wall Checkpoint Left");
        wallCheckpointRight = GetComponentInChildren<Transform>().Find("Wall Checkpoint Right");
        groundCheckpointLeft = GetComponentInChildren<Transform>().Find("Ground Checkpoint Left");
        groundCheckpointRight = GetComponentInChildren<Transform>().Find("Ground Checkpoint Right");
        defaultGravityScale = playerRb.gravityScale;
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
        CheckGround();
        CheckWalls();
        Vector2 movementInput = controls.Player.Move.ReadValue<Vector2>();

        /* move */
        Movement(movementInput);

        /* Wall-stick */
        WallStick(movementInput);

        /* fall */
        Fall();

        /* jump */
        Jump();

    }

    void CheckGround()
    {
        RaycastHit2D leftHit = Physics2D.Raycast(groundCheckpointLeft.transform.position, Vector2.down, groundCheckDistance, groundLayer);
        RaycastHit2D rightHit = Physics2D.Raycast(groundCheckpointRight.transform.position, Vector2.down, groundCheckDistance, groundLayer);
        isGrounded = (leftHit.collider != null) || (rightHit.collider != null);
        if (isGrounded && playerStatus != PlayerStatus.WallJumping)
        {
            playerStatus = PlayerStatus.Grounded;
        }
        // this else-if is a safety measure, but it only sets the status to Airborne so it's prone to bugs.
        else if (!isGrounded && playerStatus == PlayerStatus.Grounded)
        {
            playerStatus = PlayerStatus.Airborne;
        }
    }

    void CheckWalls()
    {
        RaycastHit2D leftHit = Physics2D.Raycast(wallCheckpointLeft.position, Vector2.left, wallCheckDistance, groundLayer);
        RaycastHit2D rightHit = Physics2D.Raycast(wallCheckpointRight.position, Vector2.right, wallCheckDistance, groundLayer);

        touchingWallLeft = leftHit.collider != null && leftHit.collider.CompareTag("Wall");
        touchingWallRight = rightHit.collider != null && rightHit.collider.CompareTag("Wall");
        bool touchingAnyWall = touchingWallLeft || touchingWallRight;

        // Never let WallJumping be interrupted by wall detection — that's the whole point of the lock
        if (playerStatus == PlayerStatus.WallJumping)
        {
            //Debug.Log("WallJumping");
            return;
        }

        if (touchingAnyWall && playerStatus == PlayerStatus.Airborne)
        {
            playerStatus = PlayerStatus.WallSliding;
            //Debug.Log("WallSliding");
        }
        else if (!touchingAnyWall && playerStatus == PlayerStatus.WallSliding)
        {
            playerStatus = PlayerStatus.Airborne;
            //Debug.Log("Reset");
        }
    }

    void Movement(Vector2 movementInput)
    {
        /* move */
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
    }

    void WallStick(Vector2 movementInput)
    {
        // checking the requirements for wall sticking
        bool pressingIntoWall = (touchingWallLeft && movementInput.x < 0f) || (touchingWallRight && movementInput.x > 0f);
        bool isWallSticking = false;
        if (playerStatus == PlayerStatus.WallSliding)
        {
            if (pressingIntoWall)
            {
                if (wallStickTimer > 0f)
                {
                    wallStickTimer -= Time.fixedDeltaTime;
                    isWallSticking = true;
                }
                // else: stick time is used up and player falls according to normal gravity
            }
            else
            {
                // reseting the timer every time the player is not sticking
                // keep the potential exploit of unsticking and sticking to reset the timer
                wallStickTimer = wallStickDuration;
            }
        }
        else
        {
            // always full when not actively WallSliding
            wallStickTimer = wallStickDuration;
        }
        // set ySpeed and gravity to 0 when sticking
        if (isWallSticking)
        {
            playerRb.gravityScale = 0f;
            playerRb.linearVelocityY = 0f;
        }
        else
        {
            playerRb.gravityScale = defaultGravityScale;
        }
    }

    void Fall()
    {
        // Snappier fall, independent of Gravity Scale
        if (playerRb.linearVelocityY < 0f)
        {
            playerRb.AddForce(Vector2.up * Physics2D.gravity.y * playerRb.gravityScale * (fallGravityMultiplier - 1f) * playerRb.mass);
        }
    }

    void Jump()
    {
        // check if WallJumping status should change
        if (playerStatus == PlayerStatus.WallJumping)
        {
            wallJumpLockTimer -= Time.fixedDeltaTime;
            if (wallJumpLockTimer <= 0f)
            {
                playerStatus = PlayerStatus.Airborne;
            }
        }

        // player cannot jump while Airborne
        if (jumpRequested && playerStatus != PlayerStatus.Airborne)
        {
            // jump for Grounded
            if (playerStatus == PlayerStatus.Grounded)
            {
                playerRb.linearVelocityY = jumpSpeed;
                playerStatus = PlayerStatus.Airborne;
            }
            // wall jumping
            else if (playerStatus == PlayerStatus.WallSliding)
            {
                float dirX = (touchingWallLeft) ? wallJumpSpeedX : -wallJumpSpeedX;
                playerRb.linearVelocity = new Vector2(dirX, wallJumpSpeedY);
                playerStatus = PlayerStatus.WallJumping;
                wallJumpLockTimer = wallJumpLockDuration;
            }

        }

        // jump tuning.: if player releases the jump button the jump stops
        if (
            (playerStatus == PlayerStatus.Airborne || playerStatus == PlayerStatus.WallSliding)
            && playerRb.linearVelocityY > 0f
            && !controls.Player.Jump.IsPressed()
            )
        {
            playerRb.linearVelocityY *= jumpCutMultiplier;
        }
        jumpRequested = false; // consume it either way, so stale presses don't fire late
    }

    /* Old ground checking
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            playerStatus = PlayerStatus.Grounded;
        }
    }
    */

}
