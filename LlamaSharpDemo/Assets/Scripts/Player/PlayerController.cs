using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Components")]
    public PlayerStats playerStats;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;

    private Rigidbody2D rb;
    private bool isGrounded;
    private float moveInput;
    public bool IsGrounded() => isGrounded;

    private SkillExecutor skillExecutor;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        playerStats = GetComponent<PlayerStats>();
        skillExecutor = GetComponent<SkillExecutor>();

        if (playerStats == null)
        {
            Debug.LogError("PlayerStats component not found!");
        }

        Debug.Log("PlayerController initialized!");
    }

    private bool inputEnabled = true;

    public bool IsInputEnabled => inputEnabled;

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled)
        {
            moveInput = 0;
            if (rb != null) rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        }
    }

    void Update()
    {
        if (!inputEnabled) return;

        moveInput = DemoInput.GetAxisRaw("Horizontal");

        if (DemoInput.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            Jump();
        }

        CheckGround();
    }

    void FixedUpdate()
    {
        if (skillExecutor != null && skillExecutor.IsDashing())
        {
            return;
        }

        if (playerStats != null)
        {
            float speed = playerStats.GetStat("MovementSpeed");
            rb.linearVelocity = new Vector2(moveInput * speed, rb.linearVelocity.y);
        }
    }

    void Jump()
    {
        if (playerStats != null)
        {
            float jumpForce = playerStats.GetStat("JumpPower");
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            Debug.Log("Jump!");
        }
    }

    void CheckGround()
    {
        if (groundCheck != null)
        {
            isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        }
        else
        {
            isGrounded = true;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
