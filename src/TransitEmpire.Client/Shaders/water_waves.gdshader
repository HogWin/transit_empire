shader_type spatial;
render_mode specular_schlick_ggx;

uniform vec3 shallow_color : source_color = vec3(0.09, 0.44, 0.64);
uniform vec3 deep_color : source_color = vec3(0.04, 0.21, 0.38);
uniform vec3 sky_horizon_color : source_color = vec3(0.52, 0.74, 0.92);
uniform vec3 foam_color : source_color = vec3(0.92, 0.97, 1.0);
uniform float wave_speed = 1.65;
uniform float wave_height = 0.024;

varying vec3 world_pos;
varying float shore_mask;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	shore_mask = COLOR.r;

	float w1 = sin(world_pos.x * 2.6 + TIME * wave_speed) * cos(world_pos.z * 2.2 + TIME * (wave_speed * 0.82));
	float w2 = sin((world_pos.x + world_pos.z) * 4.2 - TIME * (wave_speed * 1.25)) * 0.45;
	// Dampen wave amplitude right at the shoreline so edges stay clean against land
	float amp = wave_height * mix(1.0, 0.35, shore_mask);
	VERTEX.y += (w1 + w2) * amp;

	// Analytical normal perturbation for crisp solar specular ripples
	float dx = cos(world_pos.x * 2.6 + TIME * wave_speed) * 2.6 * amp;
	float dz = -sin(world_pos.z * 2.2 + TIME * (wave_speed * 0.82)) * 2.2 * amp;
	NORMAL = normalize(vec3(-dx, 1.0, -dz));
}

void fragment() {
	float ripple1 = sin(world_pos.x * 3.8 + TIME * wave_speed) * cos(world_pos.z * 3.8 - TIME * wave_speed);
	float ripple2 = sin(world_pos.x * 7.5 - TIME * wave_speed * 1.3 + world_pos.z * 5.2);
	float t = clamp(ripple1 * 0.5 + 0.5, 0.0, 1.0);

	// Blend deep water in open channel with brighter turquoise near shores (encoded in COLOR.r)
	vec3 water_col = mix(deep_color, shallow_color, clamp(t * 0.45 + shore_mask * 0.70, 0.0, 1.0));

	// Fresnel sky reflection at grazing camera angles
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.2);
	water_col = mix(water_col, sky_horizon_color, fresnel * 0.42);

	// Animated shoreline & bridge-pillar foam bands
	float shore_wave = sin(shore_mask * 14.0 - TIME * 2.8 + ripple2 * 1.1);
	float shore_foam = smoothstep(0.28, 0.82, shore_mask) * smoothstep(0.15, 0.65, shore_wave * 0.5 + 0.5);
	float crest_foam = smoothstep(0.84, 0.96, t * 0.7 + (ripple2 * 0.5 + 0.5) * 0.3) * 0.35;
	float total_foam = clamp(shore_foam + crest_foam, 0.0, 1.0);

	ALBEDO = mix(water_col, foam_color, total_foam);
	ROUGHNESS = mix(0.09, 0.65, total_foam);
	METALLIC = mix(0.18, 0.02, total_foam);
	SPECULAR = mix(0.88, 0.25, total_foam);
}
