using System.ComponentModel;
using UnityEngine;

namespace Sized;

public class Config
{
    public bool Debug { get; set; } = false;

    [Description("The maximum size a player can spawn with.")]
    public Vector3 MaximumSize { get; set; } = new(1f, 1f, 1f);

    [Description("The minimum size a player can spawn with.")]
    public Vector3 MinimumSize { get; set; } = new(0.7f, 0.7f, 0.7f);

    [Description("Should the random size only be applied to humans?")]
    public bool OnlyHumans { get; set; } = true;
}