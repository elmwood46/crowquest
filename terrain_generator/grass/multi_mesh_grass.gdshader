shader_type spatial;
render_mode cull_disabled;

#include "multimesh_sway.gdshaderinc"
varying float wind_roughness;

void vertex() {
	NORMAL = UP;
	vec4 calc_verts = wind_sway_vertices(VERTEX, UV, TIME, MODEL_MATRIX, INSTANCE_CUSTOM);
	VERTEX = calc_verts.xyz;
	wind_roughness = calc_verts.w;
	COLOR = mix(col_bottom,col_top,UV.y);
}

void fragment() {
	if (!FRONT_FACING) {NORMAL=-NORMAL;}
	ALBEDO = COLOR.rgb;
	SPECULAR = 0.5;
	ROUGHNESS = clamp(1.0- (roughness_factor*wind_roughness),0.5,1.0);
}