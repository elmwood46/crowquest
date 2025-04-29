extends DirectionalLight3D

@export var cycle_light = true

var t = 0
var wait_t = 0

var state = "wait"
var getting_brighter = false
@export var day_transition_time = 5
@export var sky_transition_time = 10
@export var wait_time_dark = 5
@export var wait_time_light = 5
var transition_time = 5
var wait_time = wait_time_light
var first_frame = true
var particle_shader = load("res://effects/gpu_fireflies_shader_mat.tres")
var butterfly_shader = load("res://effects/butterfly/butterfly_particles_shader_mat.tres")

func _ready() -> void:
	light_energy = 1
	wait_time = wait_time_light
	transition_time = day_transition_time

func _set_light_energy(f: float) -> void:
	light_energy = f
	particle_shader.set_shader_parameter("force_alpha_value", 1-f)
	butterfly_shader.set_shader_parameter("force_alpha_value", f)
	
func _process(delta: float) -> void:
	if (!cycle_light):
		return
	if (first_frame):
		light_energy = 1
		first_frame = false
	#var l_label = $"../../Player/Control2/PanelContainer/VBoxContainer/VBoxContainer/VBoxContainer/LightEnergyLabel"
	#l_label.text = str(light_energy)
	#l_label.text += str($"..".environment.sky.sky_material.energy_multiplier)
	#l_label.text += "\ngetting_brighter: "+str(getting_brighter)
	#l_label.text+="\nstate: "+state
	#l_label.text+="\nt: "+str(t)
	#l_label.text+="\nwait_t: "+str(wait_t)
	
	if (state=="transition" || state=="transition_sky"):
		if (state == "transition"):
			transition_time = day_transition_time
		else:
			transition_time = sky_transition_time
		t += delta
		var cosval = (cos(PI*t/transition_time)+1)/2
		var light_val = 1-cosval if getting_brighter else cosval
		if state=="transition":
			light_energy = light_val
			particle_shader.set_shader_parameter("force_alpha_value", 1-light_val)
			butterfly_shader.set_shader_parameter("force_alpha_value", light_val)
		else:
			$"..".environment.sky.sky_material.energy_multiplier = light_val
		if (t >= transition_time):
			t = 0
			if (getting_brighter):
				wait_time = wait_time_light
				if state=="transition": light_energy = 1
				else: $"..".environment.sky.sky_material.energy_multiplier = 1
			else:
				wait_time = wait_time_dark
				if state=="transition": light_energy = 0
				else: $"..".environment.sky.sky_material.energy_multiplier = 0

			if (state == "transition" && getting_brighter == false):
				state = "transition_sky"
			elif (state == "transition_sky" && getting_brighter == true):
				state = "transition"
			else:
				getting_brighter = !getting_brighter
				state = "wait"
	else:
		wait_t+=delta
		if (wait_t >= wait_time):
			wait_t = 0
			state = "transition_sky" if (getting_brighter) else "transition"
