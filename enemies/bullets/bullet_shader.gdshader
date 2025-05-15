shader_type spatial;
render_mode cull_disabled;

void vertex()
{
	//COLOR = vec4(1,1,1,1);
}

void fragment() {
	ALBEDO = COLOR.rgb;
	EMISSION = COLOR.rgb * 5.0;  // strong red glow
}