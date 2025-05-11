@tool
class_name GenMeshTest
extends MeshInstance3D

var params : MeshConvexDecompositionSettings

func _ready() -> void:
	params = MeshConvexDecompositionSettings.new()
	params.convex_hull_approximation = true
	params.convex_hull_downsampling = 1            # Lower = higher detail (default is 4)
	params.plane_downsampling = 1                  # Lower = more planes checked = better accuracy (default is 4)
	params.max_convex_hulls = 8                   # Higher allows better concave approximation
	params.resolution = 50000                    # Higher = more accurate voxelization (default is 10,000)
	params.max_num_vertices_per_convex_hull = 64            # Max vertices per convex hull; 64 is safe
	params.min_volume_per_convex_hull = 0.0001              # Prevents small pieces from being ignored
	params.max_concavity = 0.0

@export var reset_params := false:
	set(val):
		if (val):
			_ready()

@export var create_convex_kids := false:
	set (val):
		if (val):
			var save_mesh = mesh
			var readable_mesh = mesh.duplicate()
			mesh = readable_mesh   
			create_multiple_convex_collisions(params)
			mesh = save_mesh

@export var clear_children := false:
	set (val):
		if (val):
			var children = get_children()
			for i in range(children.size()):
				children[i].queue_free()
