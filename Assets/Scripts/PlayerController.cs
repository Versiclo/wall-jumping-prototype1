using UnityEngine;

public enum PlayerStatus {Grounded, Airborne, WallSliding, WallJumping}

public class PlayerController : MonoBehaviour
{
    [SerializeField] PlayerStatus playerStatus;
    Rigidbody2D playerRb;
    InputSystem_Actions controls;
    [SerializeField] float maxRunSpeed = 5f;
    [SerializeField] float moveForce = 20f;
    [SerializeField] float jumpForce = 20f;
    [SerializeField] float wallJumpX = 1.0f;
    [SerializeField] float wallJumpY = 0.7f;
    [SerializeField] float airBorneForceRestriction = 0.6f;
    [SerializeField] float wallJumpLockDuration = 0.2f;
    float wallJumpLockTimer;
    bool jumpRequested = false;

    [SerializeField] float wallCheckDistance = 0.2f;
    // two transforms on right and left edges of the player that cast ray to check for walls
    [SerializeField] Transform wallCheckpointLeft;
    [SerializeField] Transform wallCheckpointRight;
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
        if (playerStatus == PlayerStatus.Grounded)
        {
            playerRb.AddForce(Vector2.right * moveForce * movementInput.x);
        }
        else if (playerStatus == PlayerStatus.Airborne || playerStatus == PlayerStatus.WallSliding)
        {
            playerRb.AddForce(Vector2.right * moveForce * movementInput.x * airBorneForceRestriction);
        }
        // restrict speed to maxRunSpeed
        playerRb.linearVelocityX = Mathf.Clamp(playerRb.linearVelocityX, -maxRunSpeed, maxRunSpeed);

        // jump

        // WallJumping: no horizontal input control at all during the lock — let the impulse carry you
        if (playerStatus == PlayerStatus.WallJumping)
        {
            wallJumpLockTimer -= Time.fixedDeltaTime;
            if (wallJumpLockTimer <= 0)
            {
                playerStatus = PlayerStatus.Airborne;
            }
        }
        
        if (jumpRequested && playerStatus != PlayerStatus.Airborne)
        {
            if (playerStatus == PlayerStatus.Grounded)
            {
                playerRb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
                playerStatus = PlayerStatus.Airborne;
            }
            else if (playerStatus == PlayerStatus.WallSliding)
            {
                Vector2 jumpDirection = touchingWallLeft ? new Vector2(wallJumpX, wallJumpY) : new Vector2(-wallJumpX, wallJumpY);
                playerRb.AddForce(jumpDirection * jumpForce, ForceMode2D.Impulse);
                playerStatus = PlayerStatus.WallJumping;
                wallJumpLockTimer = wallJumpLockDuration;
                
            }
        }
        jumpRequested = false;  // consume it either way, so stale presses don't fire late

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
