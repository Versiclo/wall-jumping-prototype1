using System;
using UnityEngine;

// wallJumping status keeps the player from sticking to another wall for a certan duration (wallJumpLockDuration)
public enum PlayerStatus {Grounded, Airborne, WallSliding, WallJumping}

// used to tag which kind of jump is currently being tracked for metrics
public enum JumpType {Grounded, WallJump}

public class PlayerController : MonoBehaviour
{
    [SerializeField] PlayerStatus playerStatus;
    Rigidbody2D playerRb;
    InputSystem_Actions controls;

    // --- Jump metric events -------------------------------------------------
    // JumpMetricsLogger (or anything else) can subscribe to these without
    // PlayerController knowing or caring that a listener exists.
    public event Action<JumpType> OnJumpStart;
    public event Action<float, float> OnApexReached; // (apex height, time to apex)
    public event Action<Vector2, float, JumpType> OnLanded; // (displacement, airtime, jump type)
    public event Action<float, float> OnReturnToLaunchHeight; // (horizontal distance, time elapsed) — fires when descending back through the launch Y, independent of where the actual ground is
    public event Action<Vector2, float> OnDied; // (position at death, time)
    // -------------------------------------------------------------------------
    
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
    [SerializeField] float wallJumpCoyoteTime = 0.1f;
    [SerializeField] float groundJumpCoyoteTime = 0.1f;
    [SerializeField] float wallStickMaxDuration = 0.5f;
    [SerializeField] float groundCheckDistance = 0.1f;
    float wallJumpLockTimer;
    float wallCoyoteTimer;
    float groundCoyoteTimer;
    float wallStickTimer;
    // this variable reads and holds the initial gravity scale for restoration
    float defaultGravityScale;
    bool jumpRequested = false;

    bool touchingWallLeft;
    bool touchingWallRight;
    bool isGrounded;
    bool lastWallSlideWasLeft;

    /* Metrics */

    // jump metrics tracking — consumed by JumpMetricsLogger (or anything else) via the events above.
    // Note: if you wall-jump before landing a grounded jump, tracking re-starts from the wall-jump
    // push-off point and re-tags the flight as WallJump — combo jumps report as the most recent jump type.
    bool isTrackingJump = false;
    bool apexReachedThisJump = false;
    JumpType currentJumpType;
    Vector2 jumpStartPos;
    float jumpStartTime;
    float previousVelocityY;
    float previousHeightDelta;
    bool equalHeightRecorded;

    // restart position
    [SerializeField] Vector3 restartPos = new Vector3(-13.5f, -3.5f, 0);

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
        GameOver();
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

        /* jump metrics: apex detection via velocity sign-change */
        CheckApex();

        /* jump metrics: horizontal distance at the moment player descends back through launch height */
        CheckEqualHeightCrossing();
    }

    void GameOver()
    {
        if (transform.position.y < -15f)
        {
            OnDied?.Invoke(transform.position, Time.time);
            isTrackingJump = false;                           
            transform.position = restartPos;
        }
    }

    void CheckGround()
    {
        RaycastHit2D leftHit = Physics2D.Raycast(groundCheckpointLeft.transform.position, Vector2.down, groundCheckDistance, groundLayer);
        RaycastHit2D rightHit = Physics2D.Raycast(groundCheckpointRight.transform.position, Vector2.down, groundCheckDistance, groundLayer);
        isGrounded = (leftHit.collider != null) || (rightHit.collider != null);
        if (isGrounded && playerStatus != PlayerStatus.WallJumping)
        {
            // jump metrics: fire landing event once, exactly like the old hasjumped flag did
            if (isTrackingJump)
            {
                Vector2 displacement = (Vector2)transform.position - jumpStartPos;
                float airTime = Time.time - jumpStartTime;
                OnLanded?.Invoke(displacement, airTime, currentJumpType);
                isTrackingJump = false;
            }

            playerStatus = PlayerStatus.Grounded;
            groundCoyoteTimer = groundJumpCoyoteTime;

        }
        // this else-if is a safety measure, but it only sets the status to Airborne so it's prone to bugs.
        else if (!isGrounded)
        {
            groundCoyoteTimer -= Time.fixedDeltaTime;
            if (playerStatus == PlayerStatus.Grounded)
            {
                playerStatus = PlayerStatus.Airborne;
            }
        }
    }

    void CheckWalls()
    {
        RaycastHit2D leftHit = Physics2D.Raycast(wallCheckpointLeft.position, Vector2.left, wallCheckDistance, groundLayer);
        RaycastHit2D rightHit = Physics2D.Raycast(wallCheckpointRight.position, Vector2.right, wallCheckDistance, groundLayer);
        
        touchingWallLeft = leftHit.collider != null;
        touchingWallRight = rightHit.collider != null;
        bool touchingAnyWall = touchingWallLeft || touchingWallRight;

        // wall-jump coyote time: stay topped off while actively sliding, count down once
        // contact is lost, so a jump press just after leaving the wall still registers.
        if (playerStatus == PlayerStatus.WallSliding)
        {
            wallCoyoteTimer = wallJumpCoyoteTime;
        }
        else
        {
            wallCoyoteTimer -= Time.fixedDeltaTime;
        }

        // Never let WallJumping be interrupted by wall detection — that's the whole point of the lock
        if (playerStatus == PlayerStatus.WallJumping)
        {
            return;
        }

        if (touchingAnyWall && playerStatus == PlayerStatus.Airborne)
        {
            playerStatus = PlayerStatus.WallSliding;
            lastWallSlideWasLeft = touchingWallLeft;
        }
        else if (!touchingAnyWall && playerStatus == PlayerStatus.WallSliding)
        {
            playerStatus = PlayerStatus.Airborne;
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
        /* using Player.Crouch for wall stick */
        float stickInput = controls.Player.Crouch.ReadValue<float>();
        bool pressingIntoWall = (touchingWallLeft && stickInput > 0f) || (touchingWallRight && stickInput > 0f);
        
        /* using wallPushing (Horizontal input) for wall stick 
        bool pressingIntoWall = (touchingWallLeft && movementInput.x < 0f) || (touchingWallRight && movementInput.x > 0f);
        */

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
                wallStickTimer = wallStickMaxDuration;
            }
        }
        else
        {
            // always full when not actively WallSliding
            wallStickTimer = wallStickMaxDuration;
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

        // player cannot jump while Airborne, unless within the coyote window
        bool canWallJumpFromCoyote = playerStatus == PlayerStatus.Airborne && wallCoyoteTimer > 0f;
        bool canGroundJumpFromCoyote = playerStatus == PlayerStatus.Airborne && groundCoyoteTimer > 0f;
        if (jumpRequested && (playerStatus != PlayerStatus.Airborne || canWallJumpFromCoyote || canGroundJumpFromCoyote))
        {
            // jump for Grounded
            if (playerStatus == PlayerStatus.Grounded || canGroundJumpFromCoyote)
            {
                //  ground jump metrics
                StartJumpTracking(JumpType.Grounded);

                playerRb.linearVelocityY = jumpSpeed;
                playerStatus = PlayerStatus.Airborne;
            }
            // wall jumping — either still sliding, or within the coyote window after leaving the wall
            else if (playerStatus == PlayerStatus.WallSliding || canWallJumpFromCoyote)
            {
                float dirX = lastWallSlideWasLeft ? wallJumpSpeedX : -wallJumpSpeedX;
                playerRb.linearVelocity = new Vector2(dirX, wallJumpSpeedY);
                playerStatus = PlayerStatus.WallJumping;
                wallJumpLockTimer = wallJumpLockDuration;
                wallCoyoteTimer = 0f; // consumed — no double-dipping before touching a wall again

                // wall jump metrics
                StartJumpTracking(JumpType.WallJump);
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

    // jump metrics: call this at the moment a jump/wall-jump is initiated
    void StartJumpTracking(JumpType type)
    {
        currentJumpType = type;
        jumpStartPos = transform.position;
        jumpStartTime = Time.time;
        apexReachedThisJump = false;
        isTrackingJump = true;
        equalHeightRecorded = false;
        previousHeightDelta = 0f; // delta is 0 at the exact instant of push-off
        // seed previousVelocityY with the about-to-be-set velocity so CheckApex()
        // doesn't false-trigger on the very first frame of the jump
        previousVelocityY = playerRb.linearVelocityY;
 
        OnJumpStart?.Invoke(type);
    }
 
    // jump metrics: detects apex via a velocity sign-change (positive -> zero/negative),
    // which is far more reliable than checking for linearVelocityY == 0f exactly.
    void CheckApex()
    {
        if (isTrackingJump && !apexReachedThisJump && previousVelocityY > 0f && playerRb.linearVelocityY <= 0f)
        {
            apexReachedThisJump = true;
            float apexHeight = transform.position.y - jumpStartPos.y;
            float timeToApex = Time.time - jumpStartTime;
            OnApexReached?.Invoke(apexHeight, timeToApex);
        }
        previousVelocityY = playerRb.linearVelocityY;
    }

    // jump metrics: fires once, when the player descends back through the height they launched from —
    // this is independent of wherever the actual ground happens to be, so it's the number to compare
    // against flat-terrain gap widths. Only checked after apex, since height-delta starts at exactly 0
    // at push-off and would otherwise false-trigger immediately.
    void CheckEqualHeightCrossing()
    {
        if (!isTrackingJump || !apexReachedThisJump || equalHeightRecorded)
        {
            return;
        }
 
        float currentHeightDelta = transform.position.y - jumpStartPos.y;
        if (previousHeightDelta > 0f && currentHeightDelta <= 0f)
        {
            float horizontalDistance = transform.position.x - jumpStartPos.x;
            float timeElapsed = Time.time - jumpStartTime;
            OnReturnToLaunchHeight?.Invoke(horizontalDistance, timeElapsed);
            equalHeightRecorded = true;
        }
        previousHeightDelta = currentHeightDelta;
    }
}
