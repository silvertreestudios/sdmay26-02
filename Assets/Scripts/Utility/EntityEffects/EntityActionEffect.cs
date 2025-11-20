using UnityEngine;

public abstract class EntityActionEffect
{
    protected uint ActionCost;

    public EntityActionEffect(uint cost)
    {
        this.ActionCost = cost;
    }

    public abstract void Invoke(GameObject target);
}

// public class StrikeAction : EntityAction
// {
//     private int damageAmount;

//     public override void Invoke(GameObject target)
//     {
//         // Grid get target
//         var healthComponent = target.GetComponent<HealthComponent>();
//         if (healthComponent != null)
//         {
//             healthComponent.TakeDamage(damageAmount);
//         }
//     }
// }

// public class Tile2
// {
//     List<EntityAction> tileEffects;

//     public OnTileEnter(GameObject entity)
//     {
//         foreach (var action in tileEffects)
//         {
//             action.Invoke(entity);
//         }
//     }
// }