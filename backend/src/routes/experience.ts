export async function onRequestGet(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    try {
        const token = request.headers.get('Authorization');

        console.log('Get experience GET request received with token:', token);
        console.log('Must match token:', env.APITOKEN);

        // Verify authorization token against environment variable
        if (!token || token !== env.APITOKEN) {
            console.log('Authorization failed');
            return new Response('Unauthorized', { status: 401 });
        }

        const url = new URL(request.url);
        const userId = url.searchParams.get('userId');

        console.log('Requested userId:', userId);

        if (!userId) {
            console.log('No userId provided');
            return new Response('userId parameter is required', { status: 400 });
        }

        console.log('Querying database for user:', userId);

        // Get experience for a specific user - return just the number
        const result = await env['zeitvertreib-data']
            .prepare('SELECT experience FROM playerdata WHERE id = ?')
            .bind(userId)
            .first();

        console.log('Database query result:', result);

        const experience = result?.experience || 0;
        console.log('Returning experience:', experience);

        return new Response(experience.toString(), {
            headers: { 'Content-Type': 'text/plain' }
        });
    } catch (error) {
        console.error('Get experience error:', error);
        return new Response('Server error', { status: 500 });
    }
}