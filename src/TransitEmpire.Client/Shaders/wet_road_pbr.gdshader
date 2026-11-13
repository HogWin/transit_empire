shader_type spatial;
render_mode specular_schlick_ggx;

uniform vec3 base_color : source_color = vec3(0.16, 0.17, 0.20);
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform float wear_amount : hint_range(0.0, 1.0) = 0.0;
uniform float snow_cover : hint_range(0.0, 1.0) = 0.0;
uniform float base_roughness : hint_range(0.1, 1.0) = 0.80;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash2d(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise2d(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(hash2d(i + vec2(0.0, 0.0)), hash2d(i + vec2(1.0, 0.0)), u.x),
		mix(hash2d(i + vec2(0.0, 1.0)), hash2d(i + vec2(1.0, 1.0)), u.x),
		u.y
	);
}

void fragment() {
	float cam_dist = length(CAMERA_POSITION_WORLD - world_pos);
	// Fine asphalt aggregate grain + tire track wear along tile axes (skip sub-pixel grain when zoomed out)
	float grain = cam_dist < 62.0 ? (noise2d(world_pos.xz * 28.0) - 0.5) * 0.035 : 0.0;
	float patch_noise = (wear_amount > 0.001 || snow_cover > 0.001) ? noise2d(world_pos.xz * 4.8) : 0.5;
	float puddle = (wetness > 0.001 || snow_cover > 0.001) ? (sin(world_pos.x * 5.5) * cos(world_pos.z * 5.5) * 0.5 + 0.5) : 0.5;
	float wet_mask = clamp(wetness * (0.6 + 0.4 * puddle), 0.0, 1.0);

	// Darkened repaired/worn patches when wear_amount > 0
	float wear_patch = smoothstep(0.48, 0.76, patch_noise) * wear_amount;
	vec3 worn_col = mix(base_color + vec3(grain), base_color * 0.72, wear_patch * 0.65);

	// Winter Blizzard snow accumulation along road shoulders and surface
	float snow_drift = smoothstep(0.28, 0.72, patch_noise * 0.65 + puddle * 0.35) * snow_cover;
	vec3 snow_col = vec3(0.92, 0.96, 1.0);
	worn_col = mix(worn_col, snow_col, snow_drift * 0.68);

	ALBEDO = mix(worn_col, worn_col * 0.58, wet_mask * (1.0 - snow_drift));
	ROUGHNESS = mix(clamp(base_roughness + wear_patch * 0.14 - grain, 0.15, 0.98), 0.12, wet_mask);
	ROUGHNESS = mix(ROUGHNESS, 0.55, snow_drift);
	METALLIC = mix(0.05, 0.38, wet_mask * (1.0 - snow_drift));
	SPECULAR = mix(0.32, 0.88, max(wet_mask, snow_drift * 0.6));
}
