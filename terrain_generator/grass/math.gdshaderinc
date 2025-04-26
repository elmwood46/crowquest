float inversemix(float a, float b, float v){
	return (v-a)/(b-a);
}

float remap(float value, float i_min, float i_max, float o_min, float o_max) {
    return o_min + (o_max - o_min) * ((value - i_min) / (i_max - i_min));
}

float rand(vec2 n) {
	return fract(sin(dot(n, vec2(12.9898, 4.1414))) * 43758.5453);
}

// function to rotate in 3 dimensions
mat4 rotation3d(vec3 axis, float angle) {
            axis = normalize(axis);
            float s = sin(angle);
            float c = cos(angle);
            float oc = 1.0 - c;
          return mat4(
			vec4(oc*axis.x*axis.x+c,oc*axis.x*axis.y-axis.z*s,oc*axis.z*axis.x+axis.y*s,0.0),
			vec4(oc*axis.x*axis.y+axis.z*s,oc*axis.y*axis.y+c,oc*axis.y*axis.z-axis.x*s,0.0),
			vec4(oc*axis.z*axis.x-axis.y*s,oc*axis.y*axis.z+axis.x*s,oc*axis.z*axis.z+c,0.0),
			vec4(0.0,0.0,0.0,1.0));
}
