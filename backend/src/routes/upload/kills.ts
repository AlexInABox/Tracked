interface KillRecord {
    attacker: string;
    target: string;
    timestamp: number;
}

export async function onRequestPost(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    try {
        const token = request.headers.get("Authorization");
        
        console.log('Upload kills POST request received with token:', token);
        console.log('Must match token:', env.APITOKEN);
        // Verify authorization token against environment variable
        if (!token || token !== env.APITOKEN) {
            return new Response("Unauthorized", { status: 401 });
        }
        
        // Parse and validate request body as array of kill records
        const requestData = await request.json() as KillRecord[];
        
        // Validate that the data is an array
        if (!Array.isArray(requestData)) {
            return new Response("Expected array of kill records", { status: 400 });
        }
        
        // Validate each kill record
        for (const kill of requestData) {
            if (typeof kill.attacker !== 'string' || typeof kill.target !== 'string') {
                return new Response("Invalid kill record: attacker and target must be strings", { status: 400 });
            }
            if (typeof kill.timestamp !== 'number' || !Number.isInteger(kill.timestamp)) {
                return new Response("Invalid kill record: timestamp must be an integer", { status: 400 });
            }
        }
        
        // Insert kill records into database
        for (const kill of requestData) {
            await env["zeitvertreib-data"]
                .prepare(`INSERT INTO kills (attacker, target, timestamp) VALUES (?, ?, ?)`)
                .bind(kill.attacker, kill.target, kill.timestamp)
                .run();
        }
        
        return Response.json({ success: true, message: "Kill data inserted successfully" });
        
    } catch (error) {
        console.error('Upload kills POST error:', error);
        if (error instanceof SyntaxError) {
            return new Response("Invalid JSON", { status: 400 });
        }
        return new Response("Server error", { status: 500 });
    }
}
