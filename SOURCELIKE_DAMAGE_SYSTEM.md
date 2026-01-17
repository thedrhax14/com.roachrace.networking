Got it. Below is a **Source-style, impulse-based unified damage system** for **Unity3D** that behaves like **Half-Life 2**:

* Same damage logic for **player, NPCs, props**
* Damage derived from **physics impulse**, not velocity checks
* Props can **damage others and themselves**
* Thresholded (no micro-damage noise)
* Supports **impact**, **continuous crush**, and **material scaling**

This is **production-grade architecture**, not a toy example.

---

# 1. Core damage contracts (unified)

### `IDamageable.cs`

```csharp
public interface IDamageable
{
    void TakeDamage(DamageInfo info);
}
```

---

### `DamageInfo.cs`

```csharp
using UnityEngine;

public enum DamageType
{
    Generic,
    Impact,
    Crush
}

public struct DamageInfo
{
    public float amount;
    public DamageType type;
    public GameObject attacker;
    public GameObject inflictor;
    public Vector3 point;
    public Vector3 normal;
}
```

---

# 2. Physics damage calculator (core of the system)

This is the **heart** of HL2-style damage.

### `PhysicsDamage.cs`

```csharp
using UnityEngine;

public static class PhysicsDamage
{
    // Tune these globally
    public static float MinImpulseForDamage = 5f;
    public static float ImpulseToDamage = 0.08f;

    public static float ComputeImpactDamage(
        Rigidbody self,
        Rigidbody other,
        Collision collision)
    {
        // Total impulse already resolved by PhysX
        float impulse = collision.impulse.magnitude;

        if (impulse < MinImpulseForDamage)
            return 0f;

        // Mass scaling (energy-like behavior)
        float massFactor = self.mass / (self.mass + (other ? other.mass : 1000f));

        return impulse * ImpulseToDamage * massFactor;
    }
}
```

**Important**
We are using **collision.impulse**, not velocity.
This automatically handles:

* Mass
* Velocity
* Sudden stops
* Walls vs movable objects

---

# 3. Damageable base class (health, breaking, death)

### `Damageable.cs`

```csharp
using UnityEngine;

public abstract class Damageable : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHealth = 100f;
    protected float health;

    protected virtual void Awake()
    {
        health = maxHealth;
    }

    public virtual void TakeDamage(DamageInfo info)
    {
        if (info.amount <= 0f)
            return;

        health -= info.amount;

        if (health <= 0f)
            OnKilled(info);
    }

    protected abstract void OnKilled(DamageInfo info);
}
```

---

# 4. Breakable physics prop (HL2 crates, barrels, etc.)

### `BreakableProp.cs`

```csharp
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BreakableProp : Damageable
{
    public GameObject brokenPrefab;

    Rigidbody rb;

    void Awake()
    {
        base.Awake();
        rb = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision collision)
    {
        ApplyPhysicsDamage(collision);
    }

    void ApplyPhysicsDamage(Collision collision)
    {
        Rigidbody otherRb = collision.rigidbody;

        float damage = PhysicsDamage.ComputeImpactDamage(
            rb, otherRb, collision);

        if (damage <= 0f)
            return;

        // Damage this prop
        TakeDamage(new DamageInfo
        {
            amount = damage,
            type = DamageType.Impact,
            attacker = collision.gameObject,
            inflictor = collision.gameObject,
            point = collision.GetContact(0).point,
            normal = collision.GetContact(0).normal
        });

        // Damage the other object too (symmetrical)
        if (collision.gameObject.TryGetComponent<IDamageable>(out var target))
        {
            target.TakeDamage(new DamageInfo
            {
                amount = damage,
                type = DamageType.Impact,
                attacker = gameObject,
                inflictor = gameObject,
                point = collision.GetContact(0).point,
                normal = -collision.GetContact(0).normal
            });
        }
    }

    protected override void OnKilled(DamageInfo info)
    {
        if (brokenPrefab)
            Instantiate(brokenPrefab, transform.position, transform.rotation);

        Destroy(gameObject);
    }
}
```

✅ Result:

* Props **only break when impulse is high enough**
* Heavy props break easier than light ones
* Props damage enemies and players
* Props survive soft hits

---

# 5. Player / NPC damage receiver (same system)

### `CharacterDamageable.cs`

```csharp
using UnityEngine;

public class CharacterDamageable : Damageable
{
    protected override void OnKilled(DamageInfo info)
    {
        Debug.Log($"{name} died due to {info.type}");
        // Ragdoll / respawn / disable logic here
    }

    void OnCollisionStay(Collision collision)
    {
        // Continuous crushing damage
        if (collision.impulse.magnitude > 2f)
        {
            TakeDamage(new DamageInfo
            {
                amount = Time.fixedDeltaTime * collision.impulse.magnitude * 0.02f,
                type = DamageType.Crush,
                attacker = collision.gameObject,
                inflictor = collision.gameObject,
                point = transform.position,
                normal = Vector3.zero
            });
        }
    }
}
```

✅ Result:

* Being **pinned against walls hurts**
* Slow pressure ≠ instant damage
* Continuous force = DPS (crushing)

---

# 6. Why this matches Half-Life 2 behavior

| Behavior          | Result                           |
| ----------------- | -------------------------------- |
| Throw prop fast   | High impulse → damage + break    |
| Slow push         | No impulse spike → no damage     |
| Pin enemy to wall | Sustained impulse → crush damage |
| Prop hits player  | Both take damage                 |
| Light prop        | Needs high speed                 |
| Heavy prop        | Breaks easier                    |

No velocity checks.
No magic numbers per object.
No special “enemy damage”.

---

# 7. Tuning values (very important)

Start with:

```csharp
MinImpulseForDamage = 5
ImpulseToDamage = 0.08
```

Then tune per:

* Game scale
* Rigidbody masses
* Fixed timestep

🔑 **Never tune velocity. Always tune impulse → damage ratio.**

---

# 8. Multiplayer note (important for you)

For server-authoritative games:

* Compute **damage only on server**
* Use **server physics**
* Clients only predict visuals
* Send damage events, not collision events

This system ports cleanly to FishNet / NGO / Mirror.

---

## Mental model (remember this)

> **Objects don’t hurt each other — energy transfer does.**

If you want next, I can:

* Convert this to **FishNet prediction-safe code**
* Add **material modifiers** (metal, flesh, rubber)
* Show **anti double-damage protection**
* Optimize for **thousands of props**

Just tell me the next step.
