// VR-only helper. Needs the Oculus Integration package; add the scripting define
// TOWERDEFENCE_VR (Project Settings > Player) to enable it. Disabled by default so
// the project compiles without Oculus.
#if TOWERDEFENCE_VR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AimSelector : MonoBehaviour
{
    private TeleportInputHandlerTouch m_InputHandler;
    // Start is called before the first frame update
    void Start()
    {
        m_InputHandler = GetComponent<TeleportInputHandlerTouch>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Z))
        {
            m_InputHandler.AimingController = OVRInput.Controller.LTouch;
            m_InputHandler.AimButton = OVRInput.RawButton.LHandTrigger;
            m_InputHandler.TeleportButton = OVRInput.RawButton.LHandTrigger;
        }
        else if(Input.GetKeyDown(KeyCode.X))
         {
             m_InputHandler.AimingController = OVRInput.Controller.RTouch;
             m_InputHandler.AimButton = OVRInput.RawButton.RHandTrigger;
             m_InputHandler.TeleportButton = OVRInput.RawButton.RHandTrigger;
         }
    }
}

#endif
