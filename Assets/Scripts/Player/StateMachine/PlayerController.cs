using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region State Variables

    public StateMachine<PlayerController> stateMachine;
    public IdleState idleState;
    public RunningState runningState;
    public JumpingState jumpingState;
    public InAirState inAirState;

    #endregion

    #region Component Variables

    public Animator animator;
    public CharacterController controller;
    public GameSettings settings;
    public PlayerInputHandler inputHandler;

    #endregion

    #region Movement Variables

    public Vector3 velocity;
    private SphereCollider groundCheckCollider; // Collider for ground check
    public float groundCheckRadius = 10f; // Radius of the sphere used to check for the ground
    public LayerMask groundLayer; // Layer mask to define what is considered ground

    public Vector3 ledgePosition;
    public Vector3 ledgeDetectedPosition;
    private bool isLedgeDetected;
    private Vector3 respawnPosition;
    private bool isGrounded;
    private float jumpBufferCounter;
    private float coyoteTimeCounter;
    private float jumpTimeCounter;
    private bool isJumping;
    private int jumpCount;
    private bool isCrouching;
    private Vector3 originalCenter;
    private float originalHeight;
    private float crouchHeight = 1f;
    private float headCheckDistance = 0.5f;
    public float pushCooldown = 0.5f; // Cooldown time in seconds
    public float lastPushTime; // Time of the last push state exit
    private float pushCheckRadius = 1f;
    public LayerMask pushableLayer;
    public PushHandle currentPushHandle;
    public Vector3 ledgeOffset = new Vector3(0f, -1f, 0.5f); // Adjustable offset for the ledge grab position

    public bool IsSneaking = false;

    public GameObject pushableObject;

    #endregion

    #region State Flags

    public bool IsIntroFinished = false;
    
    #endregion


    #region Unity Callbacks

    private void Start()
    {
        InitializeStateMachine();
        InitializeCursor();
        InitializeSound();
        InitializeChecks();

        // Assuming the player has a MeshRenderer component
        if (playerRenderer != null)
        {
            meshMaterial = playerRenderer.material;
        }

        if (settings.debugMode)
        {
            Debug.Log("PlayerController initialized.");
        }
    }

    void Update()
    {
        HandleJumpBuffer();
        HandleCoyoteTime();

        stateMachine.Update();
    }

    void FixedUpdate()
    {
        stateMachine.FixedUpdate();
    }

    void LateUpdate()
    {
        stateMachine.LateUpdate();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, groundCheckRadius);

        // Draw ground check sphere
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position + Vector3.down * 0.5f, 0.1f);

        // Draw ledge check ray
        Gizmos.color = Color.blue;
        Vector3 origin = transform.position + Vector3.up * 2.0f;
        Vector3 direction = transform.forward * 1.5f;
        Gizmos.DrawRay(origin, direction);

        if (isLedgeDetected)
        {
            // Draw detected ledge position
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(ledgeDetectedPosition, 0.1f);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PushHandle handle = other.GetComponent<PushHandle>();
        if (handle != null && handle.pushableObject != null)
        {
            currentPushHandle = handle;
        }
        if (other.CompareTag("Deadly"))
        {
            stateMachine.ChangeState(deathState);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PushHandle handle = other.GetComponent<PushHandle>();
        if (handle != null && handle.pushableObject != null)
        {
            currentPushHandle = null;
        }
    }

    #endregion

    #region Initialization Functions

    private void InitializeStateMachine()
    {
        CalculateJumpParameters();
        stateMachine        = new StateMachine<PlayerController>(settings.debugMode);

        startState          = new StartState(this);
        standState          = new StandState(this);
        idleState           = new IdleState(this);
        pauseState          = new PauseState(this);
        runningState        = new RunningState(this);
        jumpingState        = new JumpingState(this);
        inAirState          = new InAirState(this);
        endState            = new EndState(this);
        crouchIdleState     = new CrouchIdleState(this);
        crouchMovingState   = new CrouchMovingState(this);
        pushingState        = new PushingState(this);
        ledgeGrabState      = new LedgeGrabState(this);
        deathState          = new DeathState(this);
        respawnState        = new RespawnState(this);
        upgradeState        = new UpgradeState(this);
        secretState         = new SecretState(this);
        menuState           = new MenuState(this);

        if(settings.skipIntro)
        {
            stateMachine.Initialize(idleState);
            startMenu.SetActive(false);
        }
        else
        {
            stateMachine.Initialize(startState);
        }
    }

    private void InitializeCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void InitializeSound()
    {
        SoundManager.instance?.PlaySound("Music");
    }

    private void InitializeChecks()
    {
        groundCheckCollider = gameObject.AddComponent<SphereCollider>();
        groundCheckCollider.isTrigger = true;
        groundCheckCollider.radius = groundCheckRadius;
        groundCheckCollider.center = new Vector3(0, 0, 0);

        originalCenter = controller.center;
        originalHeight = controller.height;
    }

    // Calculates the jump parameters based on settings
    private void CalculateJumpParameters()
    {
        settings.gravity = -2 * settings.jumpHeight / Mathf.Pow(settings.jumpDuration, 2);
        settings.jumpForce = 2 * settings.jumpHeight / settings.jumpDuration;
    }

    #endregion

    #region Movement Functions

    // Checks if the player is grounded
    public bool IsGrounded()
    {
        // Use a sphere cast to check for ground
        Collider[] colliders = Physics.OverlapSphere(transform.position + groundCheckCollider.center, groundCheckCollider.radius, groundLayer);
        return controller.isGrounded || colliders.Length > 0;
    }

    // Handles gravity for the player
    public void HandleGravity()
    {
        isGrounded = IsGrounded();

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; // Small negative value to keep the player grounded
        }

        if (velocity.y < 0)
        {
            velocity.y += settings.extraFallGravity * Time.deltaTime;
        }
        else if (isJumping && inputHandler.Jump && jumpTimeCounter > 0)
        {
            jumpTimeCounter -= Time.deltaTime;
            velocity.y += settings.jumpHoldGravity * Time.deltaTime;
        }
        else
        {
            velocity.y += settings.gravity * Time.deltaTime;
        }

        controller.Move(velocity * Time.deltaTime);
    }

    // Handles rotation based on input
    public void HandleRotation()
    {
        Vector2 moveInput = inputHandler.Move;
        if (moveInput != Vector2.zero)
        {
            Vector3 moveDirection = new Vector3(moveInput.x, 0, moveInput.y);

            Vector3 forward = cameraTransform.forward;
            Vector3 right = cameraTransform.right;
            forward.y = 0;
            right.y = 0;
            forward.Normalize();
            right.Normalize();

            Vector3 desiredDirection = forward * moveDirection.z + right * moveDirection.x;

            if (desiredDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(desiredDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * settings.rotationSpeed);
            }
        }
    }

    #endregion

    #region Jump Functions

    // Handles jump buffer logic
    private void HandleJumpBuffer()
    {
        if (inputHandler.Jump)
        {
            jumpBufferCounter = settings.jumpBufferTime;
        }
        else if (jumpBufferCounter > 0)
        {
            jumpBufferCounter -= Time.deltaTime;
        }
    }

    // Handles coyote time logic
    private void HandleCoyoteTime()
    {
        if (isGrounded)
        {
            coyoteTimeCounter = settings.coyoteTime;
        }
        else if (coyoteTimeCounter > 0)
        {
            coyoteTimeCounter -= Time.deltaTime;
        }
    }

    // Checks if the player can jump
    public bool CanJump()
    {
        if (settings.debugMode)
        {
            Debug.Log("Grounded:" + isGrounded);
            Debug.Log("Coyote:" + (coyoteTimeCounter > 0));
            Debug.Log("Jump Buffer:" + (jumpBufferCounter > 0));
        }
       
        return jumpCount > 0 && jumpBufferCounter > 0 && (isGrounded || coyoteTimeCounter > 0);
    }

    // Starts the jump process
    public void StartJump()
    {
        isJumping = true;
        jumpTimeCounter = settings.jumpHoldTime;
        jumpCount--;
    }

    // Ends the jump process
    public void EndJump()
    {
        isJumping = false;
    }

    // Resets the jump buffer
    public void ResetJumpBuffer()
    {
        jumpBufferCounter = 0f;
    }

    // Resets the jump count (base 1)
    public void ResetJumpCount()
    {
        jumpCount = 1;
    }

    // Enables jump functionality
    public void EnableJump()
    {
        JumpEnabled = true;
    }

    // Enables sneak functionality
    public void EnableSneak()
    {
        SneakEnabled = true;
    }

    // Enables push functionality
    public void EnablePush()
    {
        PushEnabled = true;
    }

    // Enables LedgeGrab functionality
    public void EnableLedgeGrab()
    {
        LedgeGrabEnabled = true;
    }

    #endregion

    #region Miscellaneous Functions

    // Moves the player based on input
    public void Move()
    {
        Vector2 moveInput = inputHandler.Move;
        Vector3 move = new Vector3(moveInput.x, 0, moveInput.y);
        controller.Move(move * Time.deltaTime * settings.moveSpeed);
    }

    // Triggers an animation event
    public void AnimationEvent(string eventName)
    {
        stateMachine.CurrentState.OnAnimationEvent(eventName);
    }

    #endregion
}
