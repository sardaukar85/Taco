using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;

/// <summary>
/// The same as the "On-Screen Button", with Pressed property.
/// </summary>

[AddComponentMenu ("Input/On-Screen Button Custom")]
public class OnScreenButtonCustom :OnScreenControl, IPointerDownHandler, IPointerUpHandler
{
    [InputControl(layout = "Button")]
    [SerializeField]
    private string m_ControlPath;

    protected override string controlPathInternal
    {
        get => m_ControlPath;
        set => m_ControlPath = value;
    }

    public bool Pressed { get; private set; }

    public void OnPointerUp (PointerEventData eventData)
    {
        SendValueToControl (0.0f);
        Pressed = false;
    }

    public void OnPointerDown (PointerEventData eventData)
    {
        SendValueToControl (1.0f);
        Pressed = true;
    }


    public void Disable ()
    {
        if (Pressed)
        {
            SendValueToControl (0.0f);
            Pressed = false;
        }
    }
}
