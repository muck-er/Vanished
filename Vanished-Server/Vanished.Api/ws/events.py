CLIENT_EVENTS = {
    'ping': 'Keepalive',
    'typing.start': 'Utilizador começou a escrever',
    'typing.stop': 'Utilizador parou de escrever',
    'message.read': 'Utilizador leu mensagens',
    'status.update': 'Atualização manual de status',
}

SERVER_EVENTS = {
    'pong': 'Resposta keepalive',
    'message.new': 'Nova mensagem recebida',
    'message.sent': 'Mensagem persistida',
    'message.delivered': 'Mensagem entregue',
    'message.read': 'Mensagem lida pelo destinatário',
    'message.deleted': 'Mensagem apagada',
    'typing.start': 'Contacto está a escrever',
    'typing.stop': 'Contacto parou de escrever',
    'user.status': 'Contacto online/offline',
    'request.new': 'Novo pedido de mensagem',
    'request.accepted': 'Pedido aceite',
    'request.rejected': 'Pedido rejeitado',
    'session.expired': 'Sessão expirada',
}
