#[compute]
#version 450

const vec3 UPVECTOR = vec3(0,1,0);
const ivec2 RIGHT = ivec2(1,0);
const float WRAPAROUND = 100.0f;

layout(local_size_x = 1024, local_size_y = 1, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict buffer Position {
    vec3 data[];
} boid_pos;

layout(set = 0, binding = 1, std430) restrict buffer Velocity {
    vec3 data[];
} boid_vel;

layout(set = 0, binding = 2, std430) restrict buffer Params {
    float num_boids;
    float image_width;
    float image_height;
    float friend_radius;
    float avoid_radius;
    float min_vel;
    float max_vel;
    float alignment_factor;
    float cohesion_factor;
    float separation_factor;
    float delta_time;
} params;

layout(rgba16f, binding = 3) uniform image2D boid_data;

void main() {
    int my_index = int(gl_GlobalInvocationID.x);
    if(my_index >= params.num_boids) return;

    vec3 my_pos = boid_pos.data[my_index];
    vec3 my_vel = boid_vel.data[my_index];
    vec3 avg_vel = vec3(0,0,0);
    vec3 midpoint = vec3(0,0,0);
    vec3 separation_vec = vec3(0,0,0);
    int avoids = 0;
    int num_friends = 0;
    
    for(int i = 0; i < params.num_boids; i++)
    {
        if(i != my_index){
            vec3 other_pos = boid_pos.data[i];
            vec3 other_vel = boid_vel.data[i];
            float dist = distance(my_pos, other_pos);
            if(dist < params.friend_radius){
                num_friends += 1;
                avg_vel += other_vel;
                midpoint += other_pos;
                if(dist < params.avoid_radius) {
                    avoids += 1;
                    separation_vec += my_pos - other_pos;
                }
            }
        }
    }

    if(num_friends > 0)
    {
        avg_vel /= num_friends;
        my_vel += normalize(avg_vel) * params.alignment_factor;

        midpoint /= num_friends;
		my_vel += normalize(midpoint - my_pos) * params.cohesion_factor;
        
        if(avoids > 0){
		    my_vel += normalize(separation_vec) * params.separation_factor;
        }
    }

    float vel_mag = length(my_vel);
    vel_mag = clamp(vel_mag, params.min_vel, params.max_vel);
    my_vel = normalize(my_vel) * vel_mag;
    my_pos += my_vel * params.delta_time;
    my_pos = vec3(mod(my_pos.x, WRAPAROUND), mod(my_pos.y, WRAPAROUND),mod(my_pos.z, WRAPAROUND));

    boid_vel.data[my_index] = my_vel;
    boid_pos.data[my_index] = my_pos;

    ivec2 pixel_pos = ivec2(int(mod(my_index*2, params.image_width)), int(my_index / params.image_height));
    imageStore(boid_data, pixel_pos,vec4(my_pos.x, my_pos.y, my_pos.z, 0));
    imageStore(boid_data, pixel_pos+RIGHT,vec4(my_vel.x, my_vel.y, my_vel.z, 0));
}