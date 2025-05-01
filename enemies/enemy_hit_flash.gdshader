shader_type spatial;
render_mode unshaded;

uniform sampler2D tex:source_color,filter_nearest,repeat_disable;
uniform vec3 flash_color:source_color = vec3(1.0);
uniform float intensity:hint_range(0.0, 1.0, 0.1) = 0.0;
uniform bool flash_enabled;
uniform bool pulse_mode;
uniform float pulse_frequency = 10.0;

void vertex() {
    // Get the camera's right and forward vectors (ignore the up vector)
    vec3 camera_right = vec3(INV_VIEW_MATRIX[0].x, 0.0, INV_VIEW_MATRIX[0].z);
    vec3 camera_forward = vec3(INV_VIEW_MATRIX[2].x, 0.0, INV_VIEW_MATRIX[2].z);

    // Normalize the vectors
    camera_right = normalize(camera_right);
    camera_forward = normalize(camera_forward);

    // Construct a rotation matrix that aligns the object to the camera
    mat3 billboard_matrix = mat3(camera_right, vec3(0.0, 1.0, 0.0), camera_forward);

    // Apply the billboard transformation to the vertex position
    VERTEX = billboard_matrix * VERTEX;
}

void fragment() {
	vec4 tex_color = texture(tex, UV);
	if (!flash_enabled)
	{
		ALBEDO = tex_color.rgb;
		ALPHA = tex_color.a;
	}
	else
	{
		if (pulse_mode)
		{
			float pulseRatio =  ((int(TIME*pulse_frequency))%2 == 0) ? 1.0:0.0;
			ALBEDO = mix(tex_color.rgb, flash_color,pulseRatio);	
		}
		else
		{
			ALBEDO = mix(tex_color.rgb, flash_color, intensity);
		}
		
		ALPHA = tex_color.a;
	}
}