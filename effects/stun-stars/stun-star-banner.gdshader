shader_type spatial;
render_mode cull_disabled;

uniform sampler2D star_albedo : filter_nearest;

void vertex() {
	// Called for every vertex the material is visible on.
}

void fragment() {
	vec2 uv = vec2(UV.x*6.0f+TIME,UV.y*2.0f);
	vec4 sample = texture(star_albedo, uv);
	ALBEDO = sample.rgb;
	ALPHA = sample.a;
	EMISSION = ALBEDO;
}

//void light() {
//	// Called for every pixel for every light affecting the material.
//	// Uncomment to replace the default light processing function with this one.
//}
