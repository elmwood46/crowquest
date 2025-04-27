@tool
class_name TestMultiMesh
extends MultiMeshInstance3D

@export var re_init := false:
    set (val): if(val): init()

func _ready() -> void:
    print("ready!")
    init()

func init() -> void:
    print("hello!")
    var multimesh = MultiMesh.new()
    multimesh.use_custom_data = true
    multimesh.mesh = SphereMesh.new()
    multimesh.transform_format = MultiMesh.TRANSFORM_3D
    multimesh.instance_count = 10

    self.multimesh = multimesh

    for i in range(multimesh.instance_count):
        var xform = Transform3D(Basis(), Vector3(i * 2.0, 0, 0))
        multimesh.set_instance_transform(i, xform)
