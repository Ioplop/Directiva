using UnityEngine;

public class SideColorGiver : MonoBehaviour
{
    [SerializeField] private new Renderer renderer;
    [SerializeField] private Material[] sideMaterials;

    /// <summary>
    /// Sets the side to either 1 or 2. 1 means blue, or player. 2 means red, or npc enemy.
    /// Any other value will throw an error in the console.
    /// </summary>
    /// <param name="side"></param>
    public void SetSide(int side)
    {
        if (side == 1)
        {
            renderer.material = sideMaterials[0];
        }
        else if (side == 2)
        {
            renderer.material = sideMaterials[1];
        }
        else
        {
            Debug.LogError("Invalid side: " + side);
        }
    }
}
