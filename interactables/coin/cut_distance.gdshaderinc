float cutDistance(vec3 planeNormal, vec3 planePos, vec3 worldvertex, sampler2D _noise) {
    float planedistance = dot(planeNormal, planePos);
    float vertexdistance = dot(planeNormal, worldvertex);

    float dist  = vertexdistance-planedistance;

    vec2 uv = worldvertex.xy/5.0;
    uv += TIME*0.05;
    float noise = texture(_noise,uv).r;
    dist += noise*0.02;
    return dist;
}