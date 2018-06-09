using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Character : MonoBehaviour {

	public Vector3 position;
	public Vector3 velocity;
	public Vector3 gravity;
	public Vector3 dir;
	public float acceleration; 
	public float brakeAcc;
	public float angularAcc;
	public float sensitivity;
	public Transform anchor;
	public Transform capsule;
	public bool grounded = true;
	public float speed;
	public float walkSpeed;
	public bool walk;
	public float radius;
	public float minOffset;
	public float maxStep;
	public bool draw_gizmos = true;
	int noPlayerMask = 255;

	public Vector3 debug_vector;
	public double debug_magnitude;

	Vector3 prevPos;
	Vector3 bottom;
	Vector3 top;

	void Start () {
		acceleration = 20f;
		brakeAcc = 20f;
		angularAcc = 10f;
		sensitivity = 100f;
		speed = velocity.magnitude;
		walkSpeed = 6f;
		walk = true;
		prevPos = transform.position;
		radius = 0.25f;
		minOffset = 0.01f;
		dir = Vector3.forward;
		gravity = new Vector3 (0, -10, 0);
		maxStep = 0.5f;
	}

	void Update () {

	}

	void FixedUpdate() {
		GroundCheck ();
		GetInput (anchor.forward, anchor.up);
		ApplyGravity ();
		Move ();
		if (draw_gizmos) {
			Debug.DrawLine (prevPos, transform.position, Color.Lerp (Color.red, Color.yellow, speed / 100f), 10f);  //drawing a character's trajectory
			Debug.DrawLine (capsule.position + capsule.up * radius, capsule.position + capsule.up * radius + velocity, Color.cyan, 0, false);	//drawing a speed vector
		}
		prevPos = transform.position;
	}

	void Move () {
		Vector3 offset = velocity * Time.deltaTime;
		float offset_mod = offset.magnitude;
		if (offset_mod < minOffset)
			return;
		if (!grounded) {
			capsule.eulerAngles = Vector3.zero;
		}
		bottom = transform.position - capsule.up * radius;
		top = transform.position + capsule.up * radius;
		Vector3 step;
		RaycastHit hit;
		while (offset.magnitude > minOffset) {
			if (offset.magnitude <= maxStep)
				step = offset;
			else
				step = offset.normalized * maxStep;
			if (step.magnitude < minOffset) //seems this thing is not needed
				break;
			if (Physics.CapsuleCast (bottom, top, radius, step.normalized, out hit, step.magnitude)) {	//if obstacle was met
				Vector3 err = step.normalized * 0.01f;	//safe distance from wall
				if (Vector3.Dot (top - bottom, hit.normal) > 0.3f) {   //if the surface was touched by the feet then follow its rotation
					bottom = hit.point + hit.normal * radius - err;
					top = hit.point + hit.normal * 3f * radius - err;
					if (draw_gizmos)
						Debug.DrawLine (hit.point, hit.point + hit.normal, Color.blue, 10f);
				} else {	//otherwise do not rotate
					bottom += step.normalized * hit.distance - err;
					top += step.normalized * hit.distance - err;
					if (draw_gizmos)
						Debug.DrawLine (hit.point, hit.point + hit.normal, Color.green, 10f);
				}
				offset -= step.normalized * hit.distance;
				offset = Vector3.ProjectOnPlane (offset, hit.normal);
				velocity = Vector3.ProjectOnPlane (velocity, hit.normal);
			} else {	//if the way seems clear then make sure it really is
				if (!Physics.CheckCapsule (bottom + step, top + step, radius, noPlayerMask) || !FixPenetration (bottom + step, top + step)) {
					bottom += step;
					top += step;
					offset -= step;
					//here will be "sticky ground" check
				}
			}
		}
		if (offset_mod > 0.015f) {	//fixing flicking when walking into a corner
			dir.x = velocity.x;
			dir.y = velocity.y;
			dir.z = velocity.z;
			dir = Vector3.ProjectOnPlane (dir, top - bottom);
			dir.Normalize ();
		}
		transform.position = (top + bottom) / 2.00f;
		capsule.rotation = Quaternion.LookRotation (dir, top - bottom);
	}

	bool FixPenetration (Vector3 point0, Vector3 point1) {	//function that tries to fix penetration of character's capsule and colliders and return true if it does
		Collider[] colliders = Physics.OverlapCapsule (point0, point1, radius, noPlayerMask);
		Vector3 solveVector = Vector3.zero;
		Vector3 direction = Vector3.zero;
		float distance;
		bool flag = false;
		for (int i = 0; i < colliders.Length; i++) {
			if (Physics.ComputePenetration (capsule.GetComponent<Collider> (), (point0 + point1) / 2.0f, capsule.transform.rotation, colliders [i], colliders [i].transform.position, colliders [i].transform.rotation, out direction, out distance)) {
				solveVector += direction * distance;
				flag = true;
			}
		}
		if (flag) {
			solveVector += solveVector.normalized * 0.01f;
			int k = 0;
			do {
				point0 += solveVector;
				point1 += solveVector;
				k++;
			} while (Physics.CheckCapsule (point0, point1, radius, noPlayerMask) && k < 10);
			bottom = point0;
			top = point1;
		}
		return flag;
	}

	void GroundCheck(){
		Vector3 down = bottom - top;
		Ray ray = new Ray (bottom, down);
		RaycastHit hit;
		if (Physics.SphereCast (ray, radius / 2f, out hit, 0.3f, noPlayerMask)) {
			grounded = true;
		} else {
			grounded = false;
		}
	}

	void ApplyGravity(){
		velocity += gravity * Time.deltaTime;
	}

	void OnDrawGizmos(){
		if (draw_gizmos) {
			Gizmos.DrawWireSphere (bottom, 0.1f);
			Gizmos.DrawWireSphere (top, 0.1f);
			Gizmos.DrawWireSphere (transform.position, 0.15f);
		}
	}

	void GetInput (Vector3 controls_forward1, Vector3 controls_up1) {
		Vector3 controls_forward = new Vector3 (controls_forward1.x, controls_forward1.y, controls_forward1.z);
		Vector3 controls_up = new Vector3 (controls_up1.x, controls_up1.y, controls_up1.z);
		speed = velocity.magnitude;
		walk = !Input.GetButton ("Run");
		Vector3 controls_right = Vector3.Cross (controls_up, controls_forward);

		anchor.Rotate (transform.up, Input.GetAxis ("Mouse X") * sensitivity * Time.deltaTime, Space.World);	//mouse controls
		anchor.Rotate (anchor.right, - Input.GetAxis ("Mouse Y") * sensitivity * Time.deltaTime, Space.World);

		if (!Input.GetButton ("Vertical") && !Input.GetButton ("Horizontal") && grounded) {		//if no input then autobrake
			velocity = Pdif (velocity, velocity.normalized * brakeAcc * Time.fixedDeltaTime);
			return;
		}

		if (Mathf.Abs(Vector3.Dot (controls_forward, capsule.up)) > 0.9) {	//fixing control issues when camera is almost collinear to character's vertical vector
			Debug.Log("foo");
			Vector3 temp = controls_up;
			controls_up = -controls_forward;
			controls_forward = temp;
		}
		int inversion = (Vector3.Dot(capsule.up, controls_up) > 0) ? 1 : -1;
		Vector3 direction = new Vector3 ();
		if (Input.GetKey ("w"))
			direction += controls_forward * inversion;
		if (Input.GetKey ("a"))
			direction -= controls_right;
		if (Input.GetKey ("s"))
			direction -= controls_forward * inversion;
		if (Input.GetKey ("d"))
			direction += controls_right;
		direction = Vector3.ProjectOnPlane (direction, capsule.up);
		direction.Normalize();
		float brake_multiplier = 1f;

		if (grounded) {
			Vector3 horizontal_speed = Vector3.ProjectOnPlane(velocity, direction);
			if (horizontal_speed.magnitude > angularAcc * Time.fixedDeltaTime)
				velocity -= horizontal_speed.normalized * angularAcc * Time.fixedDeltaTime;
			else
				velocity -= horizontal_speed;
			if (Vector3.Dot (velocity, direction) < -0.9)	//multiplier for manual braking
				brake_multiplier = 5f;
		}

		if (walk) {
			Vector3 new_velocity = velocity + direction * acceleration * Time.fixedDeltaTime * brake_multiplier;
			if (new_velocity.magnitude < walkSpeed)
				velocity = new_velocity;
			else if (new_velocity.magnitude < speed)
				velocity = new_velocity;
			else
				velocity = new_velocity.normalized * speed;
		}
		else
			velocity += direction * acceleration * Time.fixedDeltaTime * brake_multiplier;
	}

	float PDif (float minued, float substrahend){
		return (minued > substrahend) ? minued - substrahend : 0;
	}

	Vector3 Pdif (Vector3 minued, Vector3 substrahend){			//only for collinear vectors
		return (minued.magnitude > substrahend.magnitude) ? minued - substrahend : Vector3.zero;
	}
}