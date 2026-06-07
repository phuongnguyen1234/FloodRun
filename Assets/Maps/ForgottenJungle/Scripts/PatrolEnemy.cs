using Core; // DeathReason
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PatrolEnemy : MonoBehaviour
{
    public enum EnemyState
    {
        Patrol,
        Chase,
        Dead
    }

    [Header("Patrol Points")]
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;

    [Header("Movement")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float chaseSpeed = 3.5f;
    [SerializeField] private float reachDistance = 0.1f;

    [Header("Detection")]
    [SerializeField] private float detectionRadius = 5f;

    [Header("Stomp")]
    [SerializeField] private float stompCheckThreshold = 0.3f;
    [SerializeField] private float bounceForce = 12f;

    [Header("Death")]
    [SerializeField] private GameObject deathEffect;
    [SerializeField] private AudioClip deathSound;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private PlayerController player;
    private EnemyState currentState = EnemyState.Patrol;
    private Transform currentTarget;

    private bool isDead = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        player = FindFirstObjectByType<PlayerController>();

        if (pointA == null || pointB == null)
        {
            Debug.LogError($"{gameObject.name}: thiếu Point A hoặc Point B", gameObject);
            enabled = false;
            return;
        }

        currentTarget = pointB;
    }

    private void Update()
    {
        if (isDead || player == null || player.IsDead) return;

        float distanceToPlayer = Vector2.Distance(transform.position, player.transform.position);

        if (distanceToPlayer <= detectionRadius)
            currentState = EnemyState.Chase;
        else
            currentState = EnemyState.Patrol;

        Flip();
    }

    private void FixedUpdate()
    {
        if (isDead) return;

        switch (currentState)
        {
            case EnemyState.Patrol:
                Patrol();
                break;

            case EnemyState.Chase:
                ChasePlayer();
                break;
        }
    }

    private bool goingRight = true;

    private void Patrol()
    {
        Transform target = goingRight ? pointB : pointA;

        float dir = target.position.x - transform.position.x;

        // Nếu gần tới điểm thì đổi hướng
        if (Mathf.Abs(dir) < 0.2f)
        {
            goingRight = !goingRight;
        }

        float moveDir = Mathf.Sign(dir);

        rb.linearVelocity = new Vector2(
            moveDir * patrolSpeed,
            rb.linearVelocity.y
        );
    }

    private void ChasePlayer()
    {
        MoveTo(player.transform.position, chaseSpeed);
    }

    private void MoveTo(Vector2 target, float speed)
    {
        float direction = Mathf.Sign(target.x - transform.position.x);

        rb.linearVelocity = new Vector2(direction * speed, rb.linearVelocity.y);
    }

    private void Flip()
    {
        Vector3 scale = transform.localScale;

        if (rb.linearVelocity.x > 0.01f)
            scale.x = Mathf.Abs(scale.x);
        else if (rb.linearVelocity.x < -0.01f)
            scale.x = -Mathf.Abs(scale.x);

        transform.localScale = scale;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;

        PlayerController hitPlayer = collision.gameObject.GetComponent<PlayerController>();
        if (hitPlayer == null || hitPlayer.IsDead) return;

        // Lấy contact point đầu tiên
        ContactPoint2D contact = collision.GetContact(0);

        /*
            contact.normal là vector pháp tuyến hướng từ enemy -> player.

            Nếu player đứng trên đầu enemy:
            normal sẽ gần (0, -1)

            Nếu player chạm từ bên hông:
            normal.x sẽ khác 0

            Nếu player chạm từ dưới lên:
            normal.y sẽ gần 1
        */

        bool stompFromAbove =
            contact.normal.y < -0.5f &&
            hitPlayer.GetComponent<Rigidbody2D>() != null &&
            hitPlayer.GetComponent<Rigidbody2D>().linearVelocity.y <= 0f;

        if (stompFromAbove)
        {
            Stomped(hitPlayer);
        }
        else
        {
            // Chạm trực tiếp vào enemy => player chết
            hitPlayer.Die();
        }
    }

    private void Stomped(PlayerController hitPlayer)
    {
        if (isDead) return;

        Rigidbody2D playerRb = hitPlayer.GetComponent<Rigidbody2D>();
        if (playerRb != null)
        {
            Vector2 velocity = playerRb.linearVelocity;
            velocity.y = bounceForce;
            playerRb.linearVelocity = velocity;
        }

        Die();
    }

    public void Die()
    {
        if (isDead) return;

        isDead = true;
        currentState = EnemyState.Dead;

        if (deathEffect != null)
            Instantiate(deathEffect, transform.position, Quaternion.identity);

        if (deathSound != null)
            AudioSource.PlayClipAtPoint(deathSound, transform.position);

        Destroy(gameObject);
    }

    private void FlipTowards(Vector3 target)
    {
        Vector3 scale = transform.localScale;

        if (target.x > transform.position.x)
            scale.x = Mathf.Abs(scale.x);
        else
            scale.x = -Mathf.Abs(scale.x);

        transform.localScale = scale;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        if (pointA != null && pointB != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(pointA.position, pointB.position);
            Gizmos.DrawSphere(pointA.position, 0.15f);
            Gizmos.DrawSphere(pointB.position, 0.15f);
        }
    }
}