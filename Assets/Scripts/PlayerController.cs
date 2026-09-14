using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("References")]
    public Animator anim;

    [Header("Movement")]
    public float jumpForce = 12f;
    public float gravityScale = 3f;
    public float moveSpeed = 10f;

    [Header("Ground Check")]
    public Vector2 groundCheckOffset = new Vector2(0f, -0.55f);
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;

    [Header("Surface Adhesion")]
    public float raycastDistance = 3f;
    public float rotationSpeed = 15f;
    [Tooltip("Downward force intensity to keep player glued to slopes")]
    public float adhesionForce = 10f;

    [Header("Backflip")]
    public float backflipRotationSpeed = 720f;

    [Header("Landing & Anti-Bounce")]
    public float uprightThreshold = 30f;
    public float landingSuppressionDuration = 0.2f;

    [Header("Cheat")]
    public bool cheatMode = false;

    private Rigidbody2D rb;
    private bool isGrounded;
    private bool isBackflipping;
    private float totalRotation;
    private bool backflipTriggered;
    private Vector2 surfaceNormal = Vector2.up;
    private float landingTimer = 0f;
    private bool suppressingLanding = false;

    //for DDA
    public System.Action OnBackflipCompleted;
    public System.Action<float> OnNearMiss;
    public System.Action OnCollision;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.gravityScale = gravityScale;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    void Update()
    {
        CheckGrounded();
        HandleJump();
        HandleBackflip();

        if (cheatMode)
        {
            OnBackflipCompleted?.Invoke();
        }
    }

    void FixedUpdate()
    {
        UpdateLandingTimer();

        if (isGrounded && !isBackflipping)
        {
            ScanSurface();
            MoveForward();
            AlignToSurface();

            if (suppressingLanding)
            {
                ApplyLandingSuppression();
            }
            else
            {
                ApplySurfaceAdhesion();
            }
        }
        else if (!isGrounded)
        {
            ApplyAirbornePhysics();
        }
    }

    private void ScanSurface()
    {
        //performs a raycast downward to detect the slope angle and normal of the terrain
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, raycastDistance, groundLayer);

        if (hit.collider != null)
        {
            surfaceNormal = hit.normal;
        }
        else
        {
            //fallback to global Up if no ground is detected within raycastDistance
            surfaceNormal = Vector2.up;
        }
    }

    private void CheckGrounded()
    {
        bool wasGrounded = isGrounded;
        Vector2 checkPos = (Vector2)transform.position + groundCheckOffset;
        isGrounded = Physics2D.OverlapCircle(checkPos, groundCheckRadius, groundLayer);

        if (!wasGrounded && isGrounded)
        {
            OnLanded();
        }
    }

    private void OnLanded()
    {
        float impactVelocityY = rb.linearVelocity.y;

        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, raycastDistance, groundLayer);
        if (hit.collider != null)
        {
            surfaceNormal = hit.normal;
        }

        isBackflipping = false;
        anim.SetBool("isBackFlipping", false);

        totalRotation = 0f;
        backflipTriggered = false;

        CheckLandingOrientation();

        if (hit.collider != null)
        {
            float targetY = hit.point.y - groundCheckOffset.y;
            rb.position = new Vector2(rb.position.x, targetY);
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        }

        if (impactVelocityY < -4f)
        {
            landingTimer = landingSuppressionDuration;
            suppressingLanding = true;
        }
    }

    private void ApplyLandingSuppression()
    {
        rb.gravityScale = 0f;

        Vector2 vel = rb.linearVelocity;
        if (vel.y > 0f)
        {
            vel.y = 0f;
            rb.linearVelocity = vel;
        }

        SnapToSurface(1.0f);
    }

    private void ApplySurfaceAdhesion()
    {
        rb.gravityScale = 0f;

        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, raycastDistance, groundLayer);
        if (hit.collider != null)
        {
            float targetY = hit.point.y - groundCheckOffset.y;
            float error = targetY - transform.position.y;

            //apply adhesion so player will stick to surface
            Vector2 vel = rb.linearVelocity;
            vel.y += Mathf.Clamp(error / Time.fixedDeltaTime, -adhesionForce, 2f);
            rb.linearVelocity = vel;
        }
    }

    private void SnapToSurface(float maxDistance)
    {
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, raycastDistance, groundLayer);
        if (hit.collider == null)
        {
            return;
        }

        //snap to surface if player is in air
        float targetY = hit.point.y - groundCheckOffset.y;
        float error = targetY - transform.position.y;

        if (Mathf.Abs(error) < maxDistance && Mathf.Abs(error) > 0.01f)
        {
            rb.MovePosition(new Vector2(rb.position.x, rb.position.y + error));
        }
    }

    private void MoveForward()
    {
        //project forward movement onto the slope tangent
        Vector2 surfaceTangent = new Vector2(surfaceNormal.y, -surfaceNormal.x).normalized;
        rb.linearVelocity = new Vector2(surfaceTangent.x * moveSpeed, rb.linearVelocity.y);
    }

    private void AlignToSurface()
    {
        float targetAngle = Mathf.Atan2(surfaceNormal.x, surfaceNormal.y) * Mathf.Rad2Deg * -1f;
        float currentAngle = transform.rotation.eulerAngles.z;
        if (currentAngle > 180f)
        {
            currentAngle -= 360f;
        }

        float smoothed = Mathf.LerpAngle(currentAngle, targetAngle, rotationSpeed * Time.fixedDeltaTime);
        transform.rotation = Quaternion.Euler(0f, 0f, smoothed);
    }

    private void ApplyAirbornePhysics()
    {
        rb.gravityScale = gravityScale;

        if (isBackflipping)
        {
            float rotationThisFrame = backflipRotationSpeed * Time.fixedDeltaTime;
            rb.MoveRotation(rb.rotation + rotationThisFrame);
            totalRotation += rotationThisFrame;

            //if backflip > 300degree then count as 1 backflip
            if (!backflipTriggered && totalRotation >= 300f)
            {
                backflipTriggered = true;
                OnBackflipCompleted?.Invoke();
            }

            //reset counter, so backflip can be counted again
            if (totalRotation >= 360f)
            {
                totalRotation -= 360f;
                backflipTriggered = false;
            }
        }
    }

    private void HandleJump()
    {
        if (isGrounded && Input.GetKeyDown(KeyCode.Space))
        {
            suppressingLanding = false;
            landingTimer = 0;
            rb.gravityScale = gravityScale;

            //keep forward momentum, but override vertical velocity
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);

            isBackflipping = true;
            anim.SetBool("isBackFlipping", true);

            totalRotation = 0f;
        }
    }

    private void HandleBackflip()
    {
        if (!isGrounded)
        {
            isBackflipping = Input.GetKey(KeyCode.Space);
            anim.SetBool("isBackFlipping", true);
        }
    }

    private void CheckLandingOrientation()
    {
        float surfaceAngle = Mathf.Atan2(surfaceNormal.x, surfaceNormal.y) * Mathf.Rad2Deg * -1f;
        float playerAngle = transform.rotation.eulerAngles.z;
        if (playerAngle > 180f)
        {
            playerAngle -= 360f;
        }


        transform.rotation = Quaternion.Euler(0f, 0f, surfaceAngle);
    }

    private void UpdateLandingTimer()
    {
        if (landingTimer > 0f)
        {
            landingTimer -= Time.fixedDeltaTime;
        }
        else
        {
            suppressingLanding = false;
        }
    }

    public void SetSpeed(float speed)
    {
        moveSpeed = speed;
    }


    void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Obstacle"))
        {
            OnCollision?.Invoke();
        }
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        if (col.CompareTag("NearMiss"))
        {
            if (cheatMode)
            {
                return;
            }

            float dist = Vector2.Distance(transform.position, col.transform.position);
            OnNearMiss?.Invoke(dist);
        }
    }

}