shader_type particles;

const ivec2 RIGHT = ivec2(1,0);

uniform sampler2D boid_data;
uniform float scale = 0.5f;

void process()
{
	ivec2 pos_image_size = textureSize(boid_data,0);
	
	ivec2 texel_pos = ivec2(
		int(mod(float(INDEX)*2.0,float(pos_image_size.x))),
		int(float(INDEX)/float(pos_image_size.y))
	);
	
	vec4 boid_pos = texelFetch(boid_data,texel_pos, 0);
	vec4 boid_vel = texelFetch(boid_data,texel_pos+RIGHT, 0);
	
	if (TIME < 1.0) TRANSFORM[3].xyz = boid_pos.xyz;
	TRANSFORM[3].xyz = boid_pos.xyz;
}