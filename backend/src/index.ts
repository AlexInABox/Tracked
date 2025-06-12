import * as uploadTimes from './routes/upload/times.js';
import * as uploadKills from './routes/upload/kills.js';

const routes: Record<string, any> = {
	'/upload/times': uploadTimes,
	'/upload/kills': uploadKills
};

export default {
	async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
		const url = new URL(request.url);
		const route = routes[url.pathname];

		if (!route) {
			return new Response("Not found", { status: 404 });
		}

		const handler =
			route[`onRequest${request.method.charAt(0).toUpperCase() + request.method.slice(1).toLowerCase()}`] ||
			route.onRequest;

		if (!handler) {
			return new Response("Method not allowed", { status: 405 });
		}

		return handler(request, env, ctx);
	}
};