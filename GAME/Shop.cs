using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shop : MonoBehaviour
{

    public TurretBlueprint standardTurret;
    public TurretBlueprint missileLauncher;
    public TurretBlueprint LaserLauncher;

    BuildManager buildManager;
    private void Start()
    {
        buildManager = BuildManager.instance;   
    }

    public void SelectStandardTurret()
    {
        Debug.Log("Standard Turret Purchased");
        buildManager.SelectTurretToBuild(standardTurret);
    }

    public void SelectAnotherTurret()
    {
        Debug.Log("Another Turret Purchased");
        buildManager.SelectTurretToBuild(missileLauncher);
    }    
    public void SelectLaserTurret()
    {
        Debug.Log("Laser Turret Purchased");
        buildManager.SelectTurretToBuild(LaserLauncher);
    }
}
