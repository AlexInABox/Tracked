export async function onRequestPost(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    try {
        const token = request.headers.get('Authorization');

        console.log('Upload username POST request received with token:');
        // Verify authorization token against environment variable
        if (!token || token !== env.APITOKEN) {
            return new Response('Unauthorized', { status: 401 });
        }

        // Parse and validate request body as dictionary with string keys and integer values
        const requestData = (await request.json()) as Record<string, number>;

        // Insert or update username used in database
        for (const [playerId, username] of Object.entries(requestData)) {
            await env['zeitvertreib-data']
                .prepare(
                    `INSERT INTO playerdata (id, username) VALUES (?, ?) 
                         ON CONFLICT(id) DO UPDATE SET username = ?`,
                )
                .bind(playerId, username, username)
                .run();
        }

        return Response.json({ success: true, message: 'Username updated successfully' });
    } catch (error) {
        console.error('Upload username POST error:', error);
        if (error instanceof SyntaxError) {
            return new Response('Invalid JSON', { status: 400 });
        }
        return new Response('Server error', { status: 500 });
    }
}
