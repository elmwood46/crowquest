shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D tex;
uniform vec4 color1 : source_color;
uniform vec4 color2 : source_color;
uniform float displ_amnt = 0.8;
uniform float displ_speed = 20.0;
uniform float force_alpha_value = 1.0;

varying vec4 vert_col;

float random (vec2 uv) {
    return fract(sin(dot(uv.xy,
        vec2(12.9898,78.233))) * 43758.5453123);
}

void vertex()
{
	float mask = 1.0 - sin(PI * UV.x);
	float rand_val = COLOR.x*10.0;
	VERTEX.y = sin(rand_val + TIME * displ_speed) * displ_amnt * mask;
	vert_col = COLOR;
}

void fragment()
{
	float rand_val = vert_col.x;
	vec4 col = texture(tex, UV) * vert_col;
	//ALPHA_SCISSOR_THRESHOLD = 0.01;
	ALBEDO = col.rgb;
	ALPHA = min(col.a,force_alpha_value);
}