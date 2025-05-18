shader_type spatial;

uniform float amount : hint_range(0,1) = 0.0;
uniform sampler2D albedo;
uniform sampler2D roughness;
uniform sampler2D normal;

void vertex() {
	// Called for every vertex the material is visible on.
}

void fragment() {
	vec3 frag_col = texture(albedo,UV).rgb;
	NORMAL = texture(normal,UV).rgb;
	ROUGHNESS = texture(roughness, UV).r;
	ALBEDO = mix(frag_col,vec3(1,0,0), amount);
	ALPHA = amount;
	// Called for every pixel the material is visible on.
}