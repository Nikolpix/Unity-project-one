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
	public bool grounded;
	public bool was_grounded;
	public bool sliding;
	public float speed;
	public float walkSpeed;
	public bool walk;
	public float radius;
	public float minOffset;
	public float maxStep;
	public bool draw_gizmos = true;
	public float min_speed_to_rotate;
	public float max_speed_to_rotate;
	public float max_steepness;
	int noPlayerMask = 255;

	public Vector3 debug_vector;
	public double debug_magnitude;

	Vector3 prevPos;
	Vector3 bottom;
	Vector3 top;

	enum State {
		walking,
		running,
		jumping,
		sliding,
		drifting,
		crouching
	}

	void Start () {
		acceleration = 10f;
		brakeAcc = 20f;
		angularAcc = 5f;
		sensitivity = 100f;
		speed = velocity.magnitude;
		walkSpeed = 10f;
		walk = true;
		prevPos = transform.position;
		radius = 0.25f;
		minOffset = 0.01f;
		dir = Vector3.forward;
		gravity = new Vector3 (0, -15, 0);
		maxStep = 0.3f;
		min_speed_to_rotate = 6f;
		max_speed_to_rotate = 10f;
		max_steepness = 50f;
		grounded = true;
		was_grounded = true;
	}

	public Vector3 bar;
	public int hitIndex;
	void Update () {
		RaycastHit hit;
		if (!Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit))
			return;

		MeshCollider meshCollider = hit.collider as MeshCollider;
		if (meshCollider == null || meshCollider.sharedMesh == null)
			return;

		Mesh mesh = meshCollider.sharedMesh;
		Vector3[] normals = mesh.normals;
		int[] triangles = mesh.triangles;
		Vector3 n0 = normals[triangles[hit.triangleIndex * 3 + 0]];
		Vector3 n1 = normals[triangles[hit.triangleIndex * 3 + 1]];
		Vector3 n2 = normals[triangles[hit.triangleIndex * 3 + 2]];
		Vector3 baryCenter = hit.barycentricCoordinate;
		Vector3 interpolatedNormal = n0 * baryCenter.x + n1 * baryCenter.y + n2 * baryCenter.z;
		interpolatedNormal = interpolatedNormal.normalized;
		Transform hitTransform = hit.collider.transform;
		interpolatedNormal = hitTransform.TransformDirection(interpolatedNormal);
		Debug.DrawRay(hit.point, interpolatedNormal);
		hitIndex = hit.triangleIndex;
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

// MAIN MOVEMENT SCRIPT

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
			if (step.magnitude < minOffset)
				break;
			if (Physics.CapsuleCast (bottom, top, radius, step.normalized, out hit, step.magnitude)) {	//if an obstacle was met
				Vector3 err = step.normalized * 0.01f;	//safe distance from the wall
				if (Vector3.Dot (top - bottom, hit.normal) > 0.3f) {   //if the surface was touched by the feet then follow its rotation
					bottom = hit.point + hit.normal * radius - err;
					top = bottom + hit.normal * 2f * radius;
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
				if (was_grounded)
					velocity = Vector3.ProjectOnPlane (velocity, hit.normal).normalized * velocity.magnitude;
				else
					velocity = Vector3.ProjectOnPlane (velocity, hit.normal);
			} else {	//if the way seems clear
				if (!Physics.CheckCapsule (bottom + step, top + step, radius, noPlayerMask) || !FixPenetration (bottom + step, top + step)) {//make sure it really is
					bottom += step;
					top += step;
					offset -= step;
					if (grounded && !sliding && Physics.Raycast (new Ray (bottom, bottom - top), radius * 2) && Physics.SphereCast (new Ray(bottom, bottom - top), radius, out hit, radius) && hit.transform.tag != "not_sticky") {	//"sticky ground" check
						Vector3 normal;
						if (Vector3.Dot (top - bottom, hit.normal) > 0.3f)
							normal = hit.normal;
						else
							normal = (top - bottom).normalized;
						bottom = hit.point + normal * radius * 1.01f;
						top = bottom + normal * radius * 2;
						SelectTriange (hit);
						if (was_grounded)
							velocity = Vector3.ProjectOnPlane (velocity, normal).normalized * velocity.magnitude;
						else
							velocity = Vector3.ProjectOnPlane (velocity, normal);
						if (draw_gizmos)
							Debug.DrawLine (hit.point, hit.point + normal, Color.magenta, 10f);
					}
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

//INPUT SCRIPT

	void GetInput (Vector3 controls_forward1, Vector3 controls_up1) {
		Vector3 controls_forward = new Vector3 (controls_forward1.x, controls_forward1.y, controls_forward1.z);
		Vector3 controls_up = new Vector3 (controls_up1.x, controls_up1.y, controls_up1.z);
		speed = velocity.magnitude;
		walk = !Input.GetButton ("Run");
		Vector3 controls_right = Vector3.Cross (controls_up, controls_forward);
		acceleration = (grounded) ? 10 : 5;

		anchor.Rotate (transform.up, Input.GetAxis ("Mouse X") * sensitivity * Time.deltaTime, Space.World);	//mouse controls
		anchor.Rotate (anchor.right, - Input.GetAxis ("Mouse Y") * sensitivity * Time.deltaTime, Space.World);

		if (!Input.GetButton ("Vertical") && !Input.GetButton ("Horizontal") && grounded && !sliding) {		//if no input then autobrake
			velocity = Pdif (velocity, velocity.normalized * brakeAcc * Time.fixedDeltaTime);
			return;
		}

		float dot1 = Vector3.Dot (controls_forward, capsule.up);	//fixing control issues when forward direction of camera is almost collinear to character's vertical vector
		if (Mathf.Abs (dot1) > 0.9) {
			Vector3 temp = controls_up;
			controls_up = -controls_forward;
			controls_forward = temp;
		} else {	//fixing control issues when right direction of camera is almost collinear to character's vertical vector
			float dot2 = Vector3.Dot (controls_right, capsule.up);
			if (Mathf.Abs (dot2) > 0.9) {
				Vector3 temp = controls_up;
				controls_up = -controls_right;
				controls_right = temp;
			}
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

//ADDITIONAL SCRIPTS

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

	void ApplyGravity(){
		if (!grounded || sliding)
			velocity += gravity * Time.deltaTime;
	}

	void OnDrawGizmos(){
		if (draw_gizmos) {
			Gizmos.DrawWireSphere (bottom, 0.1f);
			Gizmos.DrawWireSphere (top, 0.1f);
			Gizmos.DrawWireSphere (transform.position, 0.15f);
		}
	}

	void GroundCheck() {
		was_grounded = grounded;
		RaycastHit hit;
		grounded = (Physics.SphereCast (new Ray (bottom, bottom - top), radius, out hit, 0.1f));
		sliding = !(Vector3.Angle (gravity, hit.normal) > 90 + max_steepness || speed > min_speed_to_rotate);
	}

	bool StickyGroundCheck () {
		RaycastHit hit;
		Vector3 offset = velocity.normalized * 0.001f;
		Vector3 normal1;
		Vector3 normal2;
		if (Physics.Raycast (new Ray (bottom + offset, bottom - top), out hit, radius * 3)) {
			normal1 = hit.normal;
			Debug.DrawLine (hit.point, hit.point + hit.normal, Color.yellow, 2f);
			if (Physics.Raycast (new Ray (bottom - offset, bottom - top * 2), out hit, radius * 3)) {
				normal2 = hit.normal;
				Debug.DrawLine (hit.point, hit.point + hit.normal, Color.yellow, 2f);
				return (Vector3.Angle (normal1, normal2) < 45f);
			} else
				return false;
		} else
			return false;
	}

	Vector3 RotationCount (Vector3 normal1, Vector3 normal2, float min_speed, float max_speed) {
		Vector3 result;
		if (speed <= min_speed)
			result = normal1;
		else if (speed >= max_speed)
			result = normal2;
		else
			result = Vector3.Lerp (normal1, normal2, (speed - min_speed) / (max_speed - min_speed));
		return result;
	}

	void SelectTriange (RaycastHit hit) {
		MeshCollider meshCollider = hit.collider as MeshCollider;
		if (meshCollider == null || meshCollider.sharedMesh == null)
			return;

		Mesh mesh = meshCollider.sharedMesh;
		Vector3[] vertices = mesh.vertices;
		int[] triangles = mesh.triangles;
		Vector3 p0 = vertices[triangles[hit.triangleIndex * 3 + 0]];
		Vector3 p1 = vertices[triangles[hit.triangleIndex * 3 + 1]];
		Vector3 p2 = vertices[triangles[hit.triangleIndex * 3 + 2]];
		Transform hitTransform = hit.collider.transform;
		p0 = hitTransform.TransformPoint(p0);
		p1 = hitTransform.TransformPoint(p1);
		p2 = hitTransform.TransformPoint(p2);
		Debug.DrawLine(p0, p1, Color.red, 0.2f);
		Debug.DrawLine(p1, p2, Color.red, 0.2f);
		Debug.DrawLine(p2, p0, Color.red, 0.2f);
	}

	Vector3 FixNormal (RaycastHit hit)
	{
		MeshCollider meshCollider = hit.collider as MeshCollider;
		if (meshCollider == null || meshCollider.sharedMesh == null)	//make sure it has mesh collider
			return hit.normal;
		int index = -1;
		int hitTriangle = hit.triangleIndex;
		Vector3[] verts = meshCollider.sharedMesh.vertices;
		int[] tris = meshCollider.sharedMesh.triangles;
		Vector3 p = meshCollider.transform.InverseTransformPoint (hit.point);
		Vector3 baryCenter = Barycenter (p, verts [tris [hitTriangle * 3 + 0]], verts [tris [hitTriangle * 3 + 1]], verts [tris [hitTriangle * 3 + 2]]); 	//make sure we hit an edge
		bar = baryCenter;
		if (baryCenter.x < 0.0001f && baryCenter.y > 0.0001f && baryCenter.z > 0.0001f) index = 0;
		else if (baryCenter.x > 0.0001f && baryCenter.y < 0.0001f && baryCenter.z > 0.0001f) index = 1;
		else if (baryCenter.x > 0.0001f && baryCenter.y > 0.0001f && baryCenter.z < 0.0001f) index = 2;
		if (index == -1)
			return hit.normal;
		int adjacentTriangle = -1;
		int vertexA;
		int vertexB;
		switch (index) {
		case 0:
			vertexA = tris [hitTriangle * 3 + 1];
			vertexB = tris [hitTriangle * 3 + 2];
			break;
		case 1:
			vertexA = tris [hitTriangle * 3 + 0];
			vertexB = tris [hitTriangle * 3 + 2];
			break;
		default:
			vertexA = tris [hitTriangle * 3 + 0];
			vertexB = tris [hitTriangle * 3 + 1];
			break;
		}
		int i = 0;
		int count = 0;
		while (i < tris.Length / 3 && adjacentTriangle == -1) {		//searching for an adjacent triangle
			count = 0;
			if (i == 79)
				Debug.Log ("lol");
			for (int j = 0; j < 3; j++)
				if ((tris[i * 3 + j] == vertexA || tris[i * 3 + j] == vertexB) && i != hitTriangle)
					count++;
			if (count == 2)
				adjacentTriangle = i;
			i ++;
		}
		if (adjacentTriangle == -1)
			return hit.normal;
		Vector3 normal1 = Vector3.Cross (verts [tris [hitTriangle * 3 + 1]] - verts [tris [hitTriangle * 3 + 0]],	//normal of hit triangle
										verts [tris [hitTriangle * 3 + 2]] - verts [tris [hitTriangle * 3 + 0]]).normalized;
		Vector3 normal2 = Vector3.Cross (verts [tris [adjacentTriangle * 3 + 1]] - verts [tris [adjacentTriangle * 3 + 0]], 	//normal of adjacent triangle
										verts [tris [adjacentTriangle * 3 + 2]] - verts [tris [adjacentTriangle * 3 + 0]]).normalized;
		return (Vector3.Angle (normal1, normal2) < 15f) ? hit.normal : normal1;
	}

	float PDif (float minued, float substrahend){
		return (minued > substrahend) ? minued - substrahend : 0;
	}

	Vector3 Pdif (Vector3 minued, Vector3 substrahend){			//only for collinear vectors
		return (minued.magnitude > substrahend.magnitude) ? minued - substrahend : Vector3.zero;
	}

	public static Vector3 Barycenter (Vector3 point, Vector3 v1, Vector3 v2, Vector3 v3) {
		Vector3 normal = Vector3.Cross (v2 - v1, v3 - v1);
		Vector3 result = new Vector3 ();
		if (Mathf.Abs (normal.z) > Mathf.Abs (normal.x) && Mathf.Abs (normal.z) > Mathf.Abs (normal.y)) {	//oh my god, I just wanna kill myself
			result.x = ((point.y - v3.y) * (v2.x - v3.x) - (point.x - v3.x) * (v2.y - v3.y)) /
				((v1.y - v3.y) * (v2.x - v3.x) - (v1.x - v3.x) * (v2.y - v3.y));
			result.y = ((point.y - v1.y) * (v3.x - v1.x) - (point.x - v1.x) * (v3.y - v1.y)) /
				((v2.y - v1.y) * (v3.x - v1.x) - (v2.x - v1.x) * (v3.y - v1.y));
			result.z = ((point.y - v1.y) * (v2.x - v1.x) - (point.x - v1.x) * (v2.y - v1.y)) /
				((v3.y - v1.y) * (v2.x - v1.x) - (v3.x - v1.x) * (v2.y - v1.y));
		} else if (Mathf.Abs (normal.y) > Mathf.Abs (normal.x) && Mathf.Abs (normal.y) > Mathf.Abs (normal.z)) {
			result.x = ((point.z - v3.z) * (v2.x - v3.x) - (point.x - v3.x) * (v2.z - v3.z)) /
				((v1.z - v3.z) * (v2.x - v3.x) - (v1.x - v3.x) * (v2.z - v3.z));
			result.y = ((point.z - v1.z) * (v3.x - v1.x) - (point.x - v1.x) * (v3.z - v1.z)) /
				((v2.z - v1.z) * (v3.x - v1.x) - (v2.x - v1.x) * (v3.z - v1.z));
			result.z = ((point.z - v1.z) * (v2.x - v1.x) - (point.x - v1.x) * (v2.z - v1.z)) /
				((v3.z - v1.z) * (v2.x - v1.x) - (v3.x - v1.x) * (v2.z - v1.z));
		} else {
			result.x = ((point.z - v3.z) * (v2.y - v3.y) - (point.y - v3.y) * (v2.z - v3.z)) /
				((v1.z - v3.z) * (v2.y - v3.y) - (v1.y - v3.y) * (v2.z - v3.z));
			result.y = ((point.z - v1.z) * (v3.y - v1.y) - (point.y - v1.y) * (v3.z - v1.z)) /
				((v2.z - v1.z) * (v3.y - v1.y) - (v2.y - v1.y) * (v3.z - v1.z));
			result.z = ((point.z - v1.z) * (v2.y - v1.y) - (point.y - v1.y) * (v2.z - v1.z)) /
				((v3.z - v1.z) * (v2.y - v1.y) - (v3.y - v1.y) * (v2.z - v1.z));
		}
		return result;
	}
}
