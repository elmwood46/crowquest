shader_type spatial;
render_mode cull_disabled;

#include "cut_distance.gdshaderinc"

uniform sampler2D _albedo : source_color;
uniform sampler2D _noise;
uniform sampler2D _spotnoise;
uniform vec4 border_colour : source_color;
uniform vec4 emission_edge : source_color;
uniform mat4 cutplane;
varying vec3 wvtx;
uniform float burnratio : hint_range(0.0, 1.0);

void vertex() {
	wvtx = (MODEL_MATRIX * vec4(VERTEX,1.0)).xyz;
}

void fragment() {
	float dist = cutDistance(-cutplane[1].xyz, cutplane[3].xyz, wvtx, _noise);

	if (dist < 0.0) discard;

	float noisediscard = texture(_spotnoise,UV).r;
    vec2 uv = wvtx.xy/5.0;
    uv += TIME*0.05;
    float noise = texture(_noise,uv).r;
    noisediscard += noise*0.05;
	if (noisediscard < burnratio) discard;

	float borderdist = smoothstep(burnratio,burnratio+0.1,noisediscard);
	vec4 alb_col = texture(_albedo,UV);

	ALBEDO = mix(border_colour, alb_col, borderdist).rgb;
	EMISSION = (mix(emission_edge*7.0, vec4(0.0), borderdist)).rgb;

}
